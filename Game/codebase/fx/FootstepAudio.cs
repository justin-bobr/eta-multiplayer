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
/// Clip library: built at _Ready by scanning res://audio/footsteps/&lt;material&gt;/. Each subfolder
/// becomes a material entry; files inside it are bucketed into Walk / Sprint / Jump / Land pools
/// based on the second-to-last underscore segment in the filename (..._walk_NN / ..._sprint_NN /
/// ..._jump_NN / ..._land_NN). The Godot group on the floor collider must match the folder name.
/// No per-instance .tscn overrides — single source of truth lives on disk under the audio root.
///
/// Async + lazy: clips are never loaded synchronously (would block scene load). On the first step
/// onto a material a threaded request is started; the subset of materials whose Godot group has
/// nodes in the loaded scene is warmed up already in _Ready (plus DefaultGroup as fallback). Steps
/// stay silent while clips are still loading (graceful). Loaded streams live in a process-wide
/// static cache — all players share one set.
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

	/// <summary>Root folder scanned at _Ready for material subdirectories. Each subdirectory
	/// becomes a material entry in the runtime library; files inside it are bucketed by their
	/// _walk_ / _sprint_ / _jump_ / _land_ filename token. Exported so a future scenario
	/// (modded surface packs, per-faction overrides) can point a specific instance at a
	/// different tree without code changes. The static library cache is built lazily on the
	/// first _Ready call and keyed by the first root it sees — diverging instances log a
	/// warning rather than re-scan.</summary>
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

	// Process-wide shared state. The audio library (folder layout) is identical for every
	// FootstepAudio instance, so we scan res://audio/footsteps/ once and reuse the parsed
	// tree across all players. _materialsTriggered, _requested, _pending and _clipCache are
	// also static so a second-spawning player skips work the first one already did.
	private static readonly Dictionary<string, Dictionary<FootstepAction, List<string>>> _lib = new();
	private static bool _libBuilt;
	private static string _libRoot;
	private static readonly HashSet<string> _materialsTriggered = new();
	private static readonly Dictionary<string, AudioStream> _clipCache = new();
	private static readonly HashSet<string> _requested = new();
	private static readonly List<string> _pending = new();
	/// <summary>Count of footstep audio clips still being asynchronously loaded across ALL FootstepAudio instances. PlayerCore polls this to gate WorldFadeOverlay's fade-out: while clips are still loading the world stays masked.</summary>
	public static int PendingLoadCount => _pending.Count;

	/// <summary>Builds the clip library and preloads only the materials actually used by colliders
	/// in the current scene (plus the default fallback and any explicit extras).</summary>
	public override void _Ready()
	{
		EnsureLibraryBuilt(AudioRoot);
		// Scene-scoped preload. Previously this called EnsureMaterialLoaded for every material in
		// the library, which finalised 3700+ AudioStreamWAV instances into ObjectDB even for maps
		// that use only 3-4 surfaces. The ObjectDB profiler flagged this as the dominant footstep
		// audio cost. Now we look at which Godot groups actually have nodes in the loaded scene
		// tree (floor colliders are tagged with a group matching the material folder name) and
		// only finalise the matching subset. Default group always finalised as the fallback for
		// colliders with no recognised group.
		var present = CollectActiveMaterials();
		foreach (string mat in present)
			EnsureMaterialLoaded(mat);

		// Phase 2: sync-drain everything that is already background-Loaded. LoadThreadedGet on a
		// terminal-status path is fast (decoded buffer → AudioStream instantiation, ~sub-ms each), but
		// _Process's 2/frame throttle would have spread 2000+ clips over ~18 s of gameplay → most
		// surfaces silent for the first 18 s. Doing it here under the fade-in mask trades that for a
		// ~1 s spawn-time hitch which is invisible because WorldFadeOverlay is opaque-black. Anything
		// genuinely still InProgress (network resource, very slow disk) stays in _pending for
		// _Process to drain at 2/frame as a fallback.
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

	/// <summary>Returns the set of material folder names that this scene's colliders actually need.
	/// A material qualifies if any node in the current scene tree is in a Godot group with that
	/// name (the floor colliders driving footstep audio are tagged with their material name).
	/// <see cref="DefaultGroup"/> is always included as the fallback for unknown surfaces.</summary>
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

	/// <summary>Idempotent guard: makes sure <see cref="BuildLibrary"/> runs exactly once
	/// per process, regardless of how many FootstepAudio nodes get spawned. The library is
	/// stateless after construction (read-only path tree) so concurrent _Ready calls from
	/// multiple players reach the same snapshot. If a second instance reports a different
	/// AudioRoot than the one already cached, the existing cache wins and we log a warning
	/// — supporting truly distinct trees per instance would require per-root caches.</summary>
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

	/// <summary>Scans the supplied root for material subdirectories: each subfolder becomes
	/// a material entry, files inside it bucket by their _walk_ / _sprint_ / _jump_ / _land_
	/// token. Static because the parsed tree is identical for every FootstepAudio instance
	/// (assuming a single AudioRoot project-wide).</summary>
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
				// In exported builds DirAccess returns the "virtual" filename without the
				// .import companion, but in editor it returns the actual on-disk entries —
				// skip .import explicitly and accept only audio extensions.
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

	/// <summary>Maps the second-to-last underscore-separated segment of a filename to a
	/// <see cref="FootstepAction"/> ("walk" / "sprint" / "jump" / "land" or the historic
	/// "landing" alias). Returns false for files that don't match the naming convention.</summary>
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

	/// <summary>Kicks off threaded loads for all clips of a material. Process-wide deduplicated
	/// via the static <see cref="_materialsTriggered"/> set, so the second player to spawn does
	/// not re-iterate any material the first player already requested. Returns whether any new
	/// path was queued so the caller can enable per-instance polling.</summary>
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

	// Pooled per-instance to keep the per-step occlusion check allocation-free. Without these,
	// every remote-player footstep created a new PhysicsRayQueryParameters3D + result Dictionary
	// (both are RefCounted -> ObjectDB churn) at ~2 Hz per remote player. With N enemies stepping
	// that is 2N allocations/sec just from occlusion - now zero after first use.
	private PhysicsRayQueryParameters3D _occlusionQuery;

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
		_occlusionQuery ??= new PhysicsRayQueryParameters3D();
		_occlusionQuery.From = ear;
		_occlusionQuery.To = sourcePos;
		_occlusionQuery.CollisionMask = OcclusionMask;
		var hit = space.IntersectRay(_occlusionQuery);
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
