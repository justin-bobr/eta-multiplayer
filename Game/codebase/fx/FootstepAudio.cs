using Godot;
using System.Collections.Generic;

/// <summary>Movement action that triggers a footstep sound, selecting which sample pool plays.</summary>
public enum FootstepAction { Walk, Sprint, Jump, Land }

/// <summary>
/// Client-side footstep audio bank. Plays material- and action-specific sounds (Walk / Sprint /
/// Jump / Land), triggered by the cadence of <see cref="FootstepController"/> and the jump/land
/// detection in <see cref="PlayerCore"/>. Purely cosmetic — gameplay-relevant cadence/loudness
/// comes from the controller (server-replayable).
///
/// Clip library: <see cref="ClipPaths"/> is a flat list of all footstep clips (res:// paths).
/// Material is the folder name; action is the filename token (..._walk_NN / ..._sprint_NN / ...).
/// On _Ready paths are grouped into material to action pools, where multiple clips per pool yield
/// random variation. The Godot group on the floor collider must match the folder name.
///
/// Async + lazy: clips are never loaded synchronously (would block scene load). On the first step
/// onto a material a threaded request is started; <see cref="PreloadGroups"/> are warmed up
/// already in _Ready. Steps stay silent while clips are still loading (graceful). Loaded streams
/// live in a process-wide static cache — all players share one set in memory.
///
/// Local vs. remote players: <see cref="IsLocalPlayer"/> = true uses a non-positional
/// AudioStreamPlayer; false uses an AudioStreamPlayer3D with distance attenuation. Loudness scales
/// hearing range (MaxDistance), not only dB. Occlusion: occluded enemy steps are detected via
/// raycast and routed through a low-pass bus. Reverb: floors in the Godot group "tunnel" route
/// through a reverb bus. The helper buses are created once via <see cref="AudioServer"/>.
/// </summary>
public partial class FootstepAudio : Node3D
{
	[Export] public bool IsLocalPlayer = true;
	[Export] public StringName Bus = "Master";
	/// <summary>Number of simultaneously sounding steps (overlap).</summary>
	[Export(PropertyHint.Range, "1,8,1")] public int PoolSize = 4;

	[ExportGroup("Clip Library")]
	/// <summary>
	/// Flat list of all footstep clips. Material is the folder name, action is the filename token
	/// (walk/sprint/jump/land). Generated from audio/footsteps/&lt;material&gt;/&lt;material&gt;_&lt;action&gt;_NN.wav.
	/// </summary>
	[Export] public string[] ClipPaths = System.Array.Empty<string>();
	/// <summary>Fallback material group when the floor collider has no recognized Godot group.</summary>
	[Export] public StringName DefaultGroup = "dirt";
	/// <summary>Materials preloaded on scene load so the first step on them is not silent.</summary>
	[Export]
	public string[] PreloadGroups =
	{
		"dirt", "concrete", "metal", "sand", "wet_sand", "mud", "grass", "wood",
		"gravel", "carpet_hard", "carpet_wood",
	};

	[ExportGroup("Mixing")]
	[Export] public float VolumeDbAtFullLoudness = 0f;
	[Export] public float VolumeDbAtMinLoudness = -20f;
	[Export] public float LandVolumeDbBoost = 3f;
	[Export(PropertyHint.Range, "0,0.4,0.01")] public float PitchRandomness = 0.10f;

	[ExportGroup("3D Audibility (remote players only)")]
	[Export] public float MinHearDistance = 11f;
	[Export] public float MaxHearDistance = 46f;
	[Export(PropertyHint.Range, "1,30,0.5")] public float UnitSize = 9f;
	[Export] public float VolumeDb3D = 4f;

	[ExportGroup("Occlusion (muffle occluded enemy steps)")]
	[Export] public bool OcclusionEnabled = true;
	[Export] public uint OcclusionMask = 1;
	[Export] public float OcclusionLowPassHz = 720f;
	[Export] public float OcclusionVolumeDb = -7f;

	[ExportGroup("Reverb (floor group 'tunnel')")]
	[Export] public bool ReverbEnabled = true;
	[Export(PropertyHint.Range, "0,1,0.05")] public float ReverbRoomSize = 0.75f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float ReverbWet = 0.45f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float ReverbDamping = 0.35f;

	private const string OccludedBusName = "FootstepOccluded";
	private const string ReverbBusName = "FootstepReverb";
	private static bool _busesReady;

	private Node[] _pool;
	private int _poolCursor;
	private readonly RandomNumberGenerator _rng = new();

	private readonly Dictionary<string, Dictionary<FootstepAction, List<string>>> _lib = new();
	private readonly HashSet<string> _materialsTriggered = new();

	private static readonly Dictionary<string, AudioStream> _clipCache = new();
	private static readonly HashSet<string> _requested = new();
	private static readonly List<string> _pending = new();
	/// <summary>Count of footstep audio clips still being asynchronously loaded across ALL FootstepAudio instances. PlayerCore polls this to gate WorldFadeOverlay's fade-out: while clips are still loading the world stays masked.</summary>
	public static int PendingLoadCount => _pending.Count;

	/// <summary>Builds the clip library and preloads the common floor materials.</summary>
	public override void _Ready()
	{
		BuildLibrary();
		// Auto-discover materials by scanning the scene tree for nodes tagged with any
		// of the material group names known to the library. Avoids the "first step on
		// material X = 40ms cold-load" hitch by kicking off the threaded import at
		// spawn, when the user is in the loading screen anyway. Materials in
		// PreloadGroups (inspector override) are additionally loaded even if not in
		// the current map — useful for grenade-bounce sounds that travel cross-area.
		SceneTree tree = GetTree();
		if (tree != null)
		{
			foreach (var kv in _lib)
			{
				string mat = kv.Key;
				if (tree.GetNodesInGroup(mat).Count > 0)
					EnsureMaterialLoaded(mat);
			}
		}
		foreach (string mat in PreloadGroups)
			if (!string.IsNullOrEmpty(mat))
				EnsureMaterialLoaded(mat);
		SetProcess(_pending.Count > 0);
		Dbg.Print($"[FootstepAudio] auto-discovered + preloaded queue: {_pending.Count} clips pending");
	}

	/// <summary>Groups <see cref="ClipPaths"/> by material (folder) and action (filename token).</summary>
	private void BuildLibrary()
	{
		foreach (string path in ClipPaths)
		{
			if (string.IsNullOrEmpty(path))
				continue;
			if (!TryParse(path, out string material, out FootstepAction action))
				continue;

			if (!_lib.TryGetValue(material, out var byAction))
				_lib[material] = byAction = new Dictionary<FootstepAction, List<string>>();
			if (!byAction.TryGetValue(action, out var list))
				byAction[action] = list = new List<string>();
			list.Add(path);
		}
	}

	/// <summary>Parses a path like res://.../&lt;material&gt;/&lt;x&gt;_&lt;action&gt;_NN.wav into material + action.</summary>
	private static bool TryParse(string path, out string material, out FootstepAction action)
	{
		material = null;
		action = FootstepAction.Walk;

		int lastSlash = path.LastIndexOf('/');
		if (lastSlash <= 0)
			return false;
		int prevSlash = path.LastIndexOf('/', lastSlash - 1);
		if (prevSlash < 0)
			return false;
		material = path.Substring(prevSlash + 1, lastSlash - prevSlash - 1);

		string file = path.Substring(lastSlash + 1);
		int dot = file.LastIndexOf('.');
		if (dot >= 0)
			file = file.Substring(0, dot);
		string[] segs = file.Split('_');
		if (segs.Length < 2)
			return false;

		switch (segs[segs.Length - 2])
		{
			case "walk":
				action = FootstepAction.Walk;
				return true;
			case "sprint":
				action = FootstepAction.Sprint;
				return true;
			case "jump":
				action = FootstepAction.Jump;
				return true;
			case "land":
			case "landing":
				action = FootstepAction.Land;
				return true;
			default:
				return false;
		}
	}

	/// <summary>Max finalizations per frame. LoadThreadedGet runs the resource-import finalization
	/// on the main thread (decodes the .wav buffer, instantiates AudioStream + sub-resources). At
	/// ~28k Godot-objects + 10MB heap per finalization on the cold path, doing N at once spikes
	/// the frame visibly. 2/frame spreads the load over a handful of frames, each spike sub-ms.</summary>
	private const int MaxFinalizationsPerFrame = 2;

	/// <summary>Polls pending threaded loads (rate-limited to <see cref="MaxFinalizationsPerFrame"/>
	/// per tick to avoid main-thread spikes) and disables itself once nothing is loading.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("FootstepAudio._Process");
		int finalizedThisFrame = 0;
		for (int i = _pending.Count - 1; i >= 0 && finalizedThisFrame < MaxFinalizationsPerFrame; i--)
		{
			string path = _pending[i];
			var status = ResourceLoader.LoadThreadedGetStatus(path);
			if (status == ResourceLoader.ThreadLoadStatus.InProgress)
				continue;

			_pending.RemoveAt(i);
			if (status == ResourceLoader.ThreadLoadStatus.Loaded
				&& ResourceLoader.LoadThreadedGet(path) is AudioStream stream)
				_clipCache[path] = stream;
			else
				GD.PushWarning($"[FootstepAudio] clip load failed: {path} ({status})");
			finalizedThisFrame++;
		}
		if (_pending.Count == 0)
			SetProcess(false);
	}

	/// <summary>Kicks off threaded loads for all clips of a material — deduped per instance and process-wide.</summary>
	private void EnsureMaterialLoaded(string material)
	{
		if (!_materialsTriggered.Add(material))
			return;
		if (!_lib.TryGetValue(material, out var byAction))
			return;

		bool added = false;
		foreach (var pool in byAction.Values)
			foreach (string path in pool)
			{
				if (_clipCache.ContainsKey(path) || !_requested.Add(path))
					continue;
				if (ResourceLoader.LoadThreadedRequest(path) == Error.Ok)
				{ _pending.Add(path); added = true; }
				else
					GD.PushWarning($"[FootstepAudio] path not loadable: {path}");
			}
		if (added)
			SetProcess(true);
	}

	/// <summary>Plays a walking/running step. <paramref name="sprinting"/> chooses Walk vs. Sprint pool.</summary>
	public void PlayStep(Vector3 worldPos, StringName material, float loudness, bool inTunnel, bool sprinting)
		=> Play(sprinting ? FootstepAction.Sprint : FootstepAction.Walk, worldPos, material, loudness, 0f, inTunnel);

	/// <summary>Plays a jump take-off sound. <paramref name="loudness"/> is 0..1 (sprinting jumps are louder).</summary>
	public void PlayJump(Vector3 worldPos, StringName material, float loudness, bool inTunnel)
		=> Play(FootstepAction.Jump, worldPos, material, loudness, 0f, inTunnel);

	/// <summary>Plays a landing sound. <paramref name="impact01"/> 0..1 scales with fall hardness.</summary>
	public void PlayLand(Vector3 worldPos, StringName material, float impact01, bool inTunnel)
		=> Play(FootstepAction.Land, worldPos, material, impact01, LandVolumeDbBoost, inTunnel);

	/// <summary>Resolves the material, ensures clips are loaded and plays the chosen action pool.</summary>
	private void Play(FootstepAction action, Vector3 worldPos, StringName material,
		float loudness01, float volumeDbBoost, bool inTunnel)
	{
		string mat = ResolveMaterial(material);
		if (mat == null)
		{
			Dbg.Print($"[FootstepAudio] {action}: group '{material}' unknown and DefaultGroup '{DefaultGroup}' not in library — no sound");
			return;
		}
		EnsureMaterialLoaded(mat);

		string played = null;
		if (_lib[mat].TryGetValue(action, out var pool))
			played = PlayClip(pool, worldPos, loudness01, volumeDbBoost, inTunnel);

		if (Dbg.Enabled)
		{
			string fallback = mat == material.ToString() ? "" : $" (fallback from '{material}')";
			string ear = IsLocalPlayer ? "local" : "remote";
			if (played != null)
				Dbg.Print($"[FootstepAudio] {action} | group '{mat}'{fallback} | {ear} | loud={loudness01:F2}{(inTunnel ? " | tunnel" : "")} → {System.IO.Path.GetFileName(played)}");
			else
				Dbg.Print($"[FootstepAudio] {action} | group '{mat}'{fallback} → clip not loaded yet, step silent");
		}
	}

	/// <summary>Maps a material to an existing pool set; unknown maps fall back to <see cref="DefaultGroup"/>.</summary>
	private string ResolveMaterial(StringName material)
	{
		string m = material.ToString();
		if (_lib.ContainsKey(m))
			return m;
		string d = DefaultGroup.ToString();
		return _lib.ContainsKey(d) ? d : null;
	}

	/// <summary>Picks a random loaded clip from the bank and plays it on the next pool slot.</summary>
	private string PlayClip(List<string> bank, Vector3 worldPos,
		float loudness01, float volumeDbBoost, bool inTunnel)
	{
		if (bank == null || bank.Count == 0)
			return null;

		int start = _rng.RandiRange(0, bank.Count - 1);
		AudioStream clip = null;
		string chosenPath = null;
		for (int i = 0; i < bank.Count; i++)
		{
			string path = bank[(start + i) % bank.Count];
			if (!string.IsNullOrEmpty(path) && _clipCache.TryGetValue(path, out var s))
			{ clip = s; chosenPath = path; break; }
		}
		if (clip == null)
			return null;

		EnsurePool();

		loudness01 = Mathf.Clamp(loudness01, 0f, 1f);
		float volumeDb = Mathf.Lerp(VolumeDbAtMinLoudness, VolumeDbAtFullLoudness, loudness01) + volumeDbBoost;
		float pitch = 1f + _rng.RandfRange(-PitchRandomness, PitchRandomness);
		bool reverb = ReverbEnabled && inTunnel;

		var node = _pool[_poolCursor];
		_poolCursor = (_poolCursor + 1) % _pool.Length;

		if (node is AudioStreamPlayer3D p3d)
		{
			p3d.MaxDistance = Mathf.Lerp(MinHearDistance, MaxHearDistance, loudness01);
			bool occluded = OcclusionEnabled && IsOccluded(worldPos);
			p3d.GlobalPosition = worldPos;
			p3d.Stream = clip;
			p3d.VolumeDb = volumeDb + VolumeDb3D + (occluded ? OcclusionVolumeDb : 0f);
			p3d.PitchScale = pitch;
			p3d.Bus = reverb ? ReverbBusName : (occluded ? OccludedBusName : Bus);
			p3d.Play();
		}
		else if (node is AudioStreamPlayer p2d)
		{
			p2d.Stream = clip;
			p2d.VolumeDb = volumeDb;
			p2d.PitchScale = pitch;
			p2d.Bus = reverb ? ReverbBusName : Bus;
			p2d.Play();
		}
		return chosenPath;
	}

	/// <summary>
	/// Raycast from the audio listener (active camera) to the sound source. A hit well before the
	/// source means the step is occluded and gets routed through the low-pass bus.
	/// </summary>
	private bool IsOccluded(Vector3 sourcePos)
	{
		Camera3D cam = GetViewport()?.GetCamera3D();
		if (cam == null)
			return false;
		Vector3 ear = cam.GlobalPosition;
		float full = ear.DistanceTo(sourcePos);
		if (full < 1.5f)
			return false;

		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null)
			return false;
		var query = PhysicsRayQueryParameters3D.Create(ear, sourcePos, OcclusionMask);
		var hit = space.IntersectRay(query);
		if (hit.Count == 0)
			return false;
		float hitDist = ear.DistanceTo((Vector3)hit["position"]);
		return hitDist < full - 1.0f;
	}

	/// <summary>
	/// Builds the audio player pool lazily on first step so <see cref="IsLocalPlayer"/> (set by
	/// PlayerCore) is guaranteed to be final, even though child _Ready runs before parent _Ready.
	/// </summary>
	private void EnsurePool()
	{
		if (_pool != null)
			return;
		EnsureHelperBuses();

		int n = Mathf.Max(1, PoolSize);
		_pool = new Node[n];
		for (int i = 0; i < n; i++)
		{
			if (IsLocalPlayer)
			{
				var p = new AudioStreamPlayer { Bus = Bus };
				AddChild(p);
				_pool[i] = p;
			}
			else
			{
				var p = new AudioStreamPlayer3D
				{
					Bus = Bus,
					MaxDistance = MaxHearDistance,
					UnitSize = UnitSize,
					AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
				};
				AddChild(p);
				_pool[i] = p;
			}
		}
	}

	/// <summary>
	/// Creates the global helper buses "FootstepOccluded" (low-pass) and "FootstepReverb" (reverb)
	/// once via AudioServer. Both send into the configured <see cref="Bus"/>.
	/// </summary>
	private void EnsureHelperBuses()
	{
		if (_busesReady)
			return;
		_busesReady = true;

		if (AudioServer.GetBusIndex(OccludedBusName) < 0)
		{
			int idx = AudioServer.BusCount;
			AudioServer.AddBus(idx);
			AudioServer.SetBusName(idx, OccludedBusName);
			AudioServer.SetBusSend(idx, Bus);
			AudioServer.AddBusEffect(idx, new AudioEffectLowPassFilter { CutoffHz = OcclusionLowPassHz });
		}
		if (AudioServer.GetBusIndex(ReverbBusName) < 0)
		{
			int idx = AudioServer.BusCount;
			AudioServer.AddBus(idx);
			AudioServer.SetBusName(idx, ReverbBusName);
			AudioServer.SetBusSend(idx, Bus);
			AudioServer.AddBusEffect(idx, new AudioEffectReverb
			{
				RoomSize = ReverbRoomSize,
				Wet = ReverbWet,
				Damping = ReverbDamping,
			});
		}
	}
}
