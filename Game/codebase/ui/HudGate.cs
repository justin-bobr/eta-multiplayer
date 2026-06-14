using System.Collections.Generic;
using Godot;

namespace Vantix.Utils;

/// <summary>Auto-hides the game-HUD layers (Hitmarker, Killfeed, Crosshair, HudCs2) while the local player
/// is frozen (team-select/preload) or dead. Meta UI (Console, NetGraph, Scoreboard, MiniProfiler) is not
/// gated. Registered HUDs are refreshed once per frame from <see cref="Tick"/>.</summary>
public static class HudGate
{
	private static readonly List<Node> _items = new();

	/// <summary>Registers a HUD root (CanvasLayer or Control) for auto-hide. Idempotent; stale references drop on next <see cref="Tick"/>.</summary>
	public static void Register(Node item)
	{
		if (item == null)
			return;
		if (_items.Contains(item))
			return;
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
			if (client == null)
				return false;
			if (!client.SpawnAuthorized)
				return false;
			if (InputGate.LocalPlayerFrozen)
				return false;
			var snap = client.LastSelfSnap;
			if (snap.HasValue && snap.Value.Hp == 0)
				return false;
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
			if (!GodotObject.IsInstanceValid(item))
			{
				_items.RemoveAt(i);
				continue;
			}
			switch (item)
			{
				case CanvasLayer cl:
					if (cl.Visible != show)
						cl.Visible = show;
					break;
				case CanvasItem ci:
					if (ci.Visible != show)
						ci.Visible = show;
					break;
			}
		}
	}
}
