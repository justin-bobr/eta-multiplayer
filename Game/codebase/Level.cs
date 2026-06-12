using Godot;
using System.Collections.Generic;

/// <summary>
/// Per-map registry, placed on the root node of every map scene (de_dust2, training, …). Holds
/// explicit <see cref="NodePath"/> arrays to the map's authored gameplay markers — <see cref="Spawn"/>s,
/// <see cref="Zone"/>s, <see cref="BombSpot"/>s and the preview <see cref="Camera3D"/>s — and resolves
/// them into typed lists once when the map enters the tree.
///
/// Replaces the old group-scan approach (a static cache walking the whole tree, PreviewCameraController
/// reading the "preview_cam" group, SpawnManager iterating the "spawn_*" groups). The mapper wires the
/// references explicitly in the inspector — or bakes them in one click with <see cref="CollectChildren"/> —
/// so there is no scene-wide walk and no per-frame group lookup at runtime.
///
/// Global access goes through <see cref="World.Level"/>: the <see cref="World"/> root script holds a
/// path to this node and exposes it statically to the HUD, bot AI, and spawn system.
/// </summary>
[Tool, GlobalClass]
public partial class Level : Node3D
{
	/// <summary>Paths (relative to this node) to every <see cref="Spawn"/> marker the map defines.</summary>
	[Export] public NodePath[] SpawnPaths { get; set; } = System.Array.Empty<NodePath>();

	/// <summary>Paths to every <see cref="Zone"/> region (HUD area names + bot nav targets).</summary>
	[Export] public NodePath[] ZonePaths { get; set; } = System.Array.Empty<NodePath>();

	/// <summary>Paths to every <see cref="BombSpot"/> (A / B / C plant regions).</summary>
	[Export] public NodePath[] BombSpotPaths { get; set; } = System.Array.Empty<NodePath>();

	/// <summary>Paths to the cinematic preview <see cref="Camera3D"/>s the team-select screen cycles.</summary>
	[Export] public NodePath[] PreviewCamPaths { get; set; } = System.Array.Empty<NodePath>();

	/// <summary>Inspector "button": tick it in the editor to (re)populate the four path arrays from this
	/// node's descendants, classified by runtime type. Reads back false so it behaves as a one-shot action
	/// rather than a stored flag.</summary>
	[Export]
	public bool CollectChildren
	{
		get => false;
		set { if (value && Engine.IsEditorHint()) BakePathsFromDescendants(); }
	}

	private readonly List<Spawn> _spawns = new();
	private readonly List<Zone> _zones = new();
	private readonly List<BombSpot> _bombSpots = new();
	private readonly List<Camera3D> _previewCams = new();
	// Zones + Spawns combined: ZoneAt() resolves a spawn-area to its name too (a player in "CT-Spawn"
	// should read "CT-Spawn" on the HUD), matching the old cache where Spawn extended Zone.
	private readonly List<Zone> _zoneLookup = new();

	/// <summary>True once <see cref="EnsureResolved"/> has turned the path arrays into live node lists.</summary>
	public bool Resolved { get; private set; }

	public IReadOnlyList<Spawn> Spawns => _spawns;
	public IReadOnlyList<Zone> Zones => _zones;
	public IReadOnlyList<BombSpot> BombSpots => _bombSpots;
	public IReadOnlyList<Camera3D> PreviewCams => _previewCams;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		EnsureResolved();
	}

	/// <summary>Resolves the exported path arrays into typed node lists. Idempotent — the first call does
	/// the work, later calls are no-ops. Called from <see cref="_Ready"/> and lazily from
	/// <see cref="World.Level"/> so a consumer that queries before the map's _Ready still gets live data.</summary>
	public void EnsureResolved()
	{
		if (Resolved) return;
		ResolveList(SpawnPaths, _spawns);
		ResolveList(ZonePaths, _zones);
		ResolveList(BombSpotPaths, _bombSpots);
		ResolveList(PreviewCamPaths, _previewCams);

		_zoneLookup.Clear();
		_zoneLookup.AddRange(_zones);
		_zoneLookup.AddRange(_spawns);

		Resolved = true;
		Dbg.Print($"[Level] {Name}: {_spawns.Count} Spawn(s), {_zones.Count} Zone(s), " +
			$"{_bombSpots.Count} BombSpot(s), {_previewCams.Count} PreviewCam(s)");
	}

	private void ResolveList<T>(NodePath[] paths, List<T> dst) where T : Node
	{
		dst.Clear();
		if (paths == null) return;
		foreach (var p in paths)
		{
			if (p == null || p.IsEmpty) continue;
			var n = GetNodeOrNull<T>(p);
			if (n != null) dst.Add(n);
			else GD.PushWarning($"[Level] {Name}: path \"{p}\" did not resolve to a {typeof(T).Name}");
		}
	}

	// === Lookups (same contracts the old cache exposed, now scoped to one map instance) ===

	/// <summary>Returns the smallest-volume <see cref="Zone"/> (or <see cref="Spawn"/> area) containing the
	/// given world position, or null when the point is outside every region. Smallest-volume = innermost
	/// nested zone wins.</summary>
	public Zone ZoneAt(Vector3 worldPos)
	{
		Zone best = null;
		float bestVol = float.MaxValue;
		foreach (var z in _zoneLookup)
		{
			if (!GodotObject.IsInstanceValid(z)) continue;
			Vector3 local = z.GlobalTransform.AffineInverse() * worldPos;
			Vector3 half = z.Size * 0.5f;
			if (Mathf.Abs(local.X) > half.X || Mathf.Abs(local.Y) > half.Y || Mathf.Abs(local.Z) > half.Z)
				continue;
			float vol = z.Size.X * z.Size.Y * z.Size.Z;
			if (vol < bestVol) { best = z; bestVol = vol; }
		}
		return best;
	}

	/// <summary>Returns the first <see cref="BombSpot"/> with the matching slot, or null when the map has
	/// none for that slot (e.g. C on a 2-site map).</summary>
	public BombSpot BombSpotForSlot(BombSpot.BombSlot slot)
	{
		foreach (var bs in _bombSpots)
			if (bs.Slot == slot) return bs;
		return null;
	}

	/// <summary>Lazy enumeration of every <see cref="Spawn"/> with the matching kind.</summary>
	public IEnumerable<Spawn> SpawnsForKind(Spawn.SpawnKind kind)
	{
		foreach (var sp in _spawns)
			if (sp.Kind == kind) yield return sp;
	}

	// === Editor baking ===

	/// <summary>Editor-only: walks this node's descendants and rewrites the four path arrays, classifying
	/// each node by runtime type. BombSpot / Spawn both extend Zone, so they're tested first.</summary>
	private void BakePathsFromDescendants()
	{
		var spawns = new List<NodePath>();
		var zones = new List<NodePath>();
		var spots = new List<NodePath>();
		var cams = new List<NodePath>();
		CollectRecursive(this, spawns, zones, spots, cams);
		SpawnPaths = spawns.ToArray();
		ZonePaths = zones.ToArray();
		BombSpotPaths = spots.ToArray();
		PreviewCamPaths = cams.ToArray();
		NotifyPropertyListChanged();
		GD.Print($"[Level] Baked: {spawns.Count} spawn, {zones.Count} zone, {spots.Count} spot, {cams.Count} cam path(s)");
	}

	private void CollectRecursive(Node node, List<NodePath> spawns, List<NodePath> zones,
		List<NodePath> spots, List<NodePath> cams)
	{
		foreach (var child in node.GetChildren())
		{
			if (child is BombSpot) spots.Add(GetPathTo(child));
			else if (child is Spawn) spawns.Add(GetPathTo(child));
			else if (child is Zone) zones.Add(GetPathTo(child));
			else if (child is Camera3D) cams.Add(GetPathTo(child));
			CollectRecursive(child, spawns, zones, spots, cams);
		}
	}
}
