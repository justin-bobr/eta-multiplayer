using Godot;
using System.Collections.Generic;

namespace Vantix.Fx;

/// <summary>Movement action that triggers a footstep sound, selecting which sample pool plays.</summary>
public enum FootstepAction { Walk, Sprint, Jump, Land }

/// <summary>
/// Client-side, cosmetic footstep audio bank. Plays material/action-specific sounds (Walk/Sprint/
/// Jump/Land) triggered by <see cref="FootstepController"/> and <see cref="NetworkPlayer"/>.
/// Library is built at _Ready by scanning res://audio/footsteps/&lt;material&gt;/; files bucket into
/// pools by the second-to-last underscore segment (..._walk_NN etc.) and the floor collider's Godot
/// group must match the folder name. Clips load async/lazily into a process-wide cache shared by all
/// players; steps stay silent until loaded.
/// Local player (<see cref="IsLocalPlayer"/>) uses a non-positional AudioStreamPlayer; remote uses
/// AudioStreamPlayer3D with distance attenuation, optional raycast occlusion (low-pass bus) and
/// "tunnel"-group reverb. Helper buses are created once via <see cref="AudioServer"/>.
/// </summary>
[Tool]
public partial class FootstepAudio : Node3D
{
	[Export] public bool IsLocalPlayer = true;
	[Export] public StringName Bus = "Master";
	/// <summary>Number of simultaneously sounding steps (overlap).</summary>
	[Export(PropertyHint.Range, "1,8,1")] public int PoolSize = 4;

	/// <summary>Root folder scanned for material subdirectories. The static library cache is keyed by
	/// the first root seen; diverging instances log a warning rather than re-scan.</summary>
	[Export] public string AudioRoot = "res://audio/footsteps";

	/// <summary>Fallback material group when the floor collider has no recognized Godot group.</summary>
	[Export] public StringName DefaultGroup = "dirt";

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

	private static readonly Dictionary<string, Dictionary<FootstepAction, List<string>>> _lib = new();
	private static bool _libBuilt;
	private static string _libRoot;
	private static readonly HashSet<string> _materialsTriggered = new();
	private static readonly Dictionary<string, AudioStream> _clipCache = new();
	private static readonly HashSet<string> _requested = new();
	private static readonly List<string> _pending = new();
	/// <summary>Count of clips still loading across all instances. NetworkPlayer polls this to gate the world fade-out.</summary>
	public static int PendingLoadCount => _pending.Count;

	/// <summary>Presents the <see cref="Bus"/> export as a dropdown of the project's audio buses.</summary>
	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		if ((string)property["name"] != nameof(Bus)) return;
		var buses = new string[AudioServer.BusCount];
		for (int i = 0; i < buses.Length; i++)
			buses[i] = AudioServer.GetBusName(i);
		property["hint"] = (int)PropertyHint.Enum;
		property["hint_string"] = string.Join(",", buses);
	}

	/// <summary>Builds the clip library and preloads only the materials actually used by colliders
	/// in the current scene (plus the default fallback and any explicit extras).</summary>
	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		EnsureLibraryBuilt(AudioRoot);
		var present = CollectActiveMaterials();
		foreach (string mat in present)
			EnsureMaterialLoaded(mat);

		// Sync-drain everything already background-Loaded under the fade-in mask; still-InProgress stays in _pending for _Process.
		int finalizedNow = 0;
		for (int i = _pending.Count - 1; i >= 0; i--)
		{
			string path = _pending[i];
			var status = ResourceLoader.LoadThreadedGetStatus(path);
			if (status == ResourceLoader.ThreadLoadStatus.InProgress) continue;
			_pending.RemoveAt(i);
			if (status == ResourceLoader.ThreadLoadStatus.Loaded
				&& ResourceLoader.LoadThreadedGet(path) is AudioStream stream)
			{
				_clipCache[path] = stream;
				finalizedNow++;
			}
			else
			{
				GD.PushWarning($"[FootstepAudio] clip load failed: {path} ({status})");
			}
		}
		SetProcess(_pending.Count > 0);
		Dbg.Print($"[FootstepAudio] materials in library={_lib.Count} | preloaded={present.Count} ({string.Join(",", present)}) | finalized-now={finalizedNow} | still-pending={_pending.Count} (will drain via _Process @ 2/frame)");
	}

	/// <summary>Returns the material folder names whose Godot group has nodes in the current scene, plus <see cref="DefaultGroup"/>.</summary>
	private HashSet<string> CollectActiveMaterials()
	{
		var present = new HashSet<string>();
		var tree = GetTree();
		foreach (string mat in _lib.Keys)
		{
			if (tree.GetNodeCountInGroup(mat) > 0)
				present.Add(mat);
		}
		string defaultMat = DefaultGroup.ToString();
		if (_lib.ContainsKey(defaultMat))
			present.Add(defaultMat);
		return present;
	}

	/// <summary>Runs <see cref="BuildLibrary"/> once per process. A second instance with a different AudioRoot reuses the existing cache and logs a warning.</summary>
	private static void EnsureLibraryBuilt(string rootPath)
	{
		if (_libBuilt)
		{
			if (_libRoot != rootPath)
				GD.PushWarning($"[FootstepAudio] AudioRoot diverges: cache was built for '{_libRoot}', new instance asked for '{rootPath}'. Reusing cache.");
			return;
		}
		_libRoot = rootPath;
		BuildLibrary(rootPath);
		_libBuilt = true;
	}

	/// <summary>Scans the root for material subdirectories; files bucket by their _walk_/_sprint_/_jump_/_land_ token.</summary>
	private static void BuildLibrary(string rootPath)
	{
		using var root = DirAccess.Open(rootPath);
		if (root == null)
		{
			GD.PushWarning($"[FootstepAudio] root folder missing: {rootPath}");
			return;
		}
		foreach (string mat in root.GetDirectories())
		{
			using var sub = DirAccess.Open($"{rootPath}/{mat}");
			if (sub == null)
				continue;
			foreach (string fileName in sub.GetFiles())
			{
				// Editor lists .import companions; skip them and accept only audio extensions.
				if (fileName.EndsWith(".import"))
					continue;
				if (!fileName.EndsWith(".wav") && !fileName.EndsWith(".ogg") && !fileName.EndsWith(".mp3"))
					continue;
				if (!TryParseAction(fileName, out FootstepAction action))
					continue;
				string path = $"{rootPath}/{mat}/{fileName}";
				if (!_lib.TryGetValue(mat, out var byAction))
					_lib[mat] = byAction = new Dictionary<FootstepAction, List<string>>();
				if (!byAction.TryGetValue(action, out var list))
					byAction[action] = list = new List<string>();
				list.Add(path);
			}
		}
	}

	/// <summary>Maps a filename's second-to-last underscore segment to a <see cref="FootstepAction"/> ("landing" aliases "land"); false if unmatched.</summary>
	private static bool TryParseAction(string fileName, out FootstepAction action)
	{
		action = FootstepAction.Walk;
		int dot = fileName.LastIndexOf('.');
		string nameOnly = dot >= 0 ? fileName.Substring(0, dot) : fileName;
		string[] segs = nameOnly.Split('_');
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

	/// <summary>Max threaded-load finalizations per frame; LoadThreadedGet finalizes on the main thread, so this caps the spike.</summary>
	private const int MaxFinalizationsPerFrame = 2;

	/// <summary>Polls pending threaded loads (rate-limited to <see cref="MaxFinalizationsPerFrame"/>
	/// per tick to avoid main-thread spikes) and disables itself once nothing is loading.</summary>
	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint()) return;
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

	/// <summary>Kicks off threaded loads for a material's clips (deduplicated via <see cref="_materialsTriggered"/>). Returns whether any new path was queued.</summary>
	private bool EnsureMaterialLoaded(string material)
	{
		if (!_materialsTriggered.Add(material))
			return false;
		if (!_lib.TryGetValue(material, out var byAction))
			return false;

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
		return added;
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

	// Reused to keep the per-step occlusion raycast allocation-free.
	private PhysicsRayQueryParameters3D _occlusionQuery;
	private readonly PhysicsRayQueryResult3D _occlusionResult = new();

	/// <summary>True if a raycast from the active camera to the source hits geometry well before it (step is occluded).</summary>
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
		_occlusionQuery ??= new PhysicsRayQueryParameters3D();
		_occlusionQuery.From = ear;
		_occlusionQuery.To = sourcePos;
		_occlusionQuery.CollisionMask = OcclusionMask;
		if (!space.IntersectRayInto(_occlusionQuery, _occlusionResult))
			return false;
		float hitDist = ear.DistanceTo(_occlusionResult.GetPosition());
		return hitDist < full - 1.0f;
	}

	/// <summary>Builds the player pool lazily on first step, so <see cref="IsLocalPlayer"/> (set by NetworkPlayer) is final.</summary>
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

	/// <summary>Creates the "FootstepOccluded" (low-pass) and "FootstepReverb" buses once; both send into <see cref="Bus"/>.</summary>
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
