using Godot;
using System.Collections.Generic;

/// <summary>Toggles the game-HUD layers (Hitmarker, Killfeed, Crosshair, HudCs2, …) on/off based on whether
/// the local player is in a live state. Hidden during team-select/preload (InputGate.LocalPlayerFrozen) and
/// while dead (LastSelfSnap.Hp == 0). Console, NetGraph, Scoreboard and MiniProfiler are intentionally not
/// gated — meta UI accessible regardless of player state. HUDs register and the gate flips their
/// <see cref="CanvasItem.Visible"/> once per frame from <see cref="Tick"/>.</summary>
public static class HudGate
{
	// CanvasLayer is not a CanvasItem in Godot 4, so items are stored as Node and the Visible setter is
	// runtime-dispatched to the right path.
	private static readonly List<Node> _items = new();

	/// <summary>Registers a HUD root (CanvasLayer or Control) for auto-hide. Idempotent; stale references drop on next <see cref="Tick"/>.</summary>
	public static void Register(Node item)
	{
		if (item == null) return;
		if (_items.Contains(item)) return;
		_items.Add(item);
	}

	/// <summary>Clears all registrations — called by NetMain on disconnect / scene-reload.</summary>
	public static void Reset() => _items.Clear();

	/// <summary>True when the game-HUD should be visible: a spawned, alive local player exists and no
	/// preload/team-select phase is active.</summary>
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

	/// <summary>Per-frame visibility refresh (from NetMain._PhysicsProcess). Drops invalid handles as it goes.</summary>
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
