using Godot;

/// <summary>
/// Central constants for all Godot InputMap actions the character reads. Previously lived as
/// [Export] StringName fields on <see cref="NetworkPlayer"/> - which is the wrong place
/// (NetworkPlayer only holds shared sim logic; input action names are game-wide constants).
///
/// Per-player customization is no longer possible here (it was unused anyway - all scenes used
/// the defaults). To change action names, go to Project Settings -> Input Map.
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
