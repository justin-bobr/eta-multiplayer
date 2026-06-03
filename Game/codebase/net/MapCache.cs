using Godot;
using System.Collections.Generic;

/// <summary>
/// Per-map cache of every authored navigation node: <see cref="Zone"/>, <see cref="BombSpot"/>,
/// and <see cref="Spawn"/>. Built once when the world scene activates (via <see cref="Scan"/>)
/// and queried at runtime by the HUD, bot AI, and other systems. Centralises the scene walk so
/// each consumer doesn't re-iterate <c>GetNodesInGroup</c> per frame.
///
/// Static state — there's only ever one active world scene at a time, so a static cache fits the
/// scope. <see cref="Reset"/> is called before a fresh scan (map switch or test reload).
///
/// Typical usage:
///   - HUD: <c>MapCache.ZoneAt(player.GlobalPosition)?.ZoneName</c> to show "you are in: B-Tunnels".
///   - Bot AI: <c>MapCache.BombSpotForSlot(BombSpot.BombSlot.A)?.GlobalPosition</c> as the goal point.
///   - SpawnManager: <c>MapCache.SpawnsForKind(Spawn.SpawnKind.Team1)</c> as a pre-typed candidate list.
///
/// The scan also collects nodes that weren't explicitly grouped — it walks the scene tree by
/// runtime type, so a future renamed group / typo doesn't silently drop a node from the cache.
/// </summary>
public static class MapCache
{
	private static readonly List<Zone> _zones = new();
	private static readonly List<BombSpot> _bombSpots = new();
	private static readonly List<Spawn> _spawns = new();

	/// <summary>All <see cref="Zone"/> nodes in the current map. Order matches scene tree DFS.</summary>
	public static IReadOnlyList<Zone> Zones => _zones;

	/// <summary>All <see cref="BombSpot"/> nodes in the current map. Order matches scene tree DFS.</summary>
	public static IReadOnlyList<BombSpot> BombSpots => _bombSpots;

	/// <summary>All <see cref="Spawn"/> nodes in the current map. Order matches scene tree DFS.</summary>
	public static IReadOnlyList<Spawn> Spawns => _spawns;

	/// <summary>True once a successful <see cref="Scan"/> has populated the cache for the active map.
	/// HUDs / bot AI gate on this so they don't query an empty cache during the brief window between
	/// world.tscn loading and the scan firing.</summary>
	public static bool Initialized { get; private set; }

	/// <summary>Walks the scene tree from the current scene root and collects every Zone, BombSpot,
	/// and Spawn node. Idempotent — safe to call again after a map switch (clears state first).
	/// Logs the counts so missing markers show up in the dev console.</summary>
	public static void Scan(SceneTree tree)
	{
		Reset();
		if (tree?.CurrentScene == null)
		{
			GD.PushWarning("[Map] Scan called with no CurrentScene — cache stays empty");
			return;
		}
		Walk(tree.CurrentScene);
		Initialized = true;
		Dbg.Print($"[Map] Scan: {_zones.Count} Zone(s), {_bombSpots.Count} BombSpot(s), {_spawns.Count} Spawn(s)");
	}

	/// <summary>Empties the cache. Call before scanning a new map or in test teardown.</summary>
	public static void Reset()
	{
		_zones.Clear();
		_bombSpots.Clear();
		_spawns.Clear();
		Initialized = false;
	}

	private static void Walk(Node node)
	{
		// Order matters: Spawn extends Zone, so the Spawn case must come BEFORE the Zone case in
		// the if/else chain. Spawn nodes go into BOTH _spawns (so SpawnManager can find them by
		// Kind) AND _zones (so ZoneAt() resolves player position to spawn-area names too — a
		// player standing in CT-Spawn-Box should see "CT-Spawn" on the HUD).
		if (node is BombSpot bs) _bombSpots.Add(bs);
		else if (node is Spawn sp) { _spawns.Add(sp); _zones.Add(sp); }
		else if (node is Zone z) _zones.Add(z);
		foreach (var child in node.GetChildren()) Walk(child);
	}

	// === Lookups ===

	/// <summary>Returns the smallest-volume <see cref="Zone"/> containing the given world position,
	/// or null when the point is outside every zone. Smallest-volume rule = innermost nested zone
	/// wins (so "B-Plant" beats the surrounding "B-Site" when both contain the player). O(zones) per
	/// call — fine for HUD use at &lt; 30 zones per map.</summary>
	public static Zone ZoneAt(Vector3 worldPos)
	{
		Zone best = null;
		float bestVol = float.MaxValue;
		foreach (var z in _zones)
		{
			Vector3 local = z.GlobalTransform.AffineInverse() * worldPos;
			Vector3 half = z.Size * 0.5f;
			if (Mathf.Abs(local.X) > half.X || Mathf.Abs(local.Y) > half.Y || Mathf.Abs(local.Z) > half.Z)
				continue;
			float vol = z.Size.X * z.Size.Y * z.Size.Z;
			if (vol < bestVol) { best = z; bestVol = vol; }
		}
		return best;
	}

	/// <summary>Returns the first <see cref="BombSpot"/> with the matching slot, or null when the
	/// map doesn't define one for that slot. Maps with only A+B return null for
	/// <see cref="BombSpot.BombSlot.C"/>.</summary>
	public static BombSpot BombSpotForSlot(BombSpot.BombSlot slot)
	{
		foreach (var bs in _bombSpots)
			if (bs.Slot == slot) return bs;
		return null;
	}

	/// <summary>Lazy enumeration of every <see cref="Spawn"/> with the matching kind. Caller can
	/// pick a random one or iterate to find a free slot.</summary>
	public static IEnumerable<Spawn> SpawnsForKind(Spawn.SpawnKind kind)
	{
		foreach (var sp in _spawns)
			if (sp.Kind == kind) yield return sp;
	}
}
