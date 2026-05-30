using Godot;
using System.Collections.Generic;

/// <summary>
/// Central gate that toggles the game-HUD layers (Hitmarker, Killfeed, LowHpFx, Crosshair,
/// HudCs2 etc.) on/off based on whether the local player is currently in a "live"-state.
/// Hidden when:
/// <list type="bullet">
///   <item><b>InputGate.LocalPlayerFrozen</b> = true — team-select / preload phase, no player yet.</item>
///   <item><b>LastSelfSnap.Hp == 0</b> — local player is dead, waiting for respawn.</item>
/// </list>
/// HUDs register themselves (or are registered centrally on spawn) and the gate flips their
/// <see cref="CanvasItem.Visible"/> once per frame from <see cref="Tick"/>. Console, NetGraph,
/// Scoreboard and MiniProfiler are intentionally NOT gated — they're meta UI that the user wants
/// to access regardless of player state (e.g. scoreboard while dead).
/// </summary>
public static class HudGate
{
	// CanvasLayer is NOT a CanvasItem in Godot 4 (it's a sibling Node-tree concept), so we widen
	// the type to Node and runtime-dispatch the Visible setter. Both CanvasLayer and CanvasItem
	// expose a bool `Visible` property — we just have to pick the right path.
	private static readonly List<Node> _items = new();

	/// <summary>Register a HUD root (CanvasLayer or top-level Control) to be auto-hidden when the player is in a non-live state. Idempotent. Stale references are dropped automatically on the next <see cref="Tick"/>.</summary>
	public static void Register(Node item)
	{
		if (item == null) return;
		if (_items.Contains(item)) return;
		_items.Add(item);
	}

	/// <summary>Clear all registrations — called by NetMain on disconnect / scene-reload so stale handles don't linger.</summary>
	public static void Reset() => _items.Clear();

	/// <summary>True when the game-HUD should currently be visible. False during the team-select
	/// phase (no LocalPlayer yet → SpawnAuthorized=false), during the preload phase
	/// (LocalPlayerFrozen=true), and while the local player is dead (Hp=0 in the latest self-
	/// snapshot). The HUD only makes sense when there's an alive player to show stats for.</summary>
	public static bool ShouldShow
	{
		get
		{
			var client = NetMain.Instance?.Client;
			if (client == null) return false;
			if (!client.SpawnAuthorized) return false;
			if (InputGate.LocalPlayerFrozen) return false;
			var snap = client.LastSelfSnap;
			if (snap.HasValue && snap.Value.Hp == 0) return false;
			return true;
		}
	}

	/// <summary>Per-frame visibility refresh. Called by NetMain._PhysicsProcess after the network polls. Drops invalid handles on the fly so QueueFree'd HUDs don't bloat the list.</summary>
	public static void Tick()
	{
		bool show = ShouldShow;
		for (int i = _items.Count - 1; i >= 0; i--)
		{
			var item = _items[i];
			if (!GodotObject.IsInstanceValid(item)) { _items.RemoveAt(i); continue; }
			switch (item)
			{
				case CanvasLayer cl: if (cl.Visible != show) cl.Visible = show; break;
				case CanvasItem ci: if (ci.Visible != show) ci.Visible = show; break;
			}
		}
	}
}
