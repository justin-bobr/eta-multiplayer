using Godot;

namespace Vantix.Utils;

/// <summary>
/// Central constants for the Godot InputMap actions the character reads. Change action names in
/// Project Settings → Input Map.
/// </summary>
public static class InputActions
{
	public static readonly StringName Forward = "forward";
	public static readonly StringName Back = "backward";
	public static readonly StringName Left = "left";
	public static readonly StringName Right = "right";
	public static readonly StringName Jump = "jump";
	public static readonly StringName Shift = "shift";
	public static readonly StringName Crouch = "crouch";
	public static readonly StringName Sprint = "run";
	public static readonly StringName Fire = "fire";
	public static readonly StringName Reload = "reload";
	public static readonly StringName Inspect = "inspect";
	public static readonly StringName Ads = "zoom";
	public static readonly StringName Breath = "breath";
	public static readonly StringName SlotWeapon = "slot_1";
	public static readonly StringName SlotGrenade = "slot_2";
	public static readonly StringName Console = "console";
}
