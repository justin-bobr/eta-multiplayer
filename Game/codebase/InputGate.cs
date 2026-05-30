using Godot;

/// <summary>
/// Central input gate. <see cref="Blocked"/> returns true when the player should
/// currently receive NO game input: settings menu open, window has no focus
/// (Alt-Tab), mouse capture off, oder LocalPlayer ist tot (HP=0, wartet auf Respawn).
///
/// All sites that read <see cref="Input.IsActionPressed"/> directly should
/// consult this gate first so keystrokes do not leak through while the settings
/// menu is open or after Alt-Tab.
/// </summary>
public static class InputGate
{
	/// <summary>Set true by LocalPlayer's _Ready while asset preloads (audio + animations) are still
	/// running, set false the same moment <c>WorldInitComplete</c> is sent. While true, both input
	/// reads (so the player can't move locally) and SendNetInput (so the server doesn't see garbage
	/// pre-spawn ticks) are skipped — the player is fully frozen until the world is ready.</summary>
	public static bool LocalPlayerFrozen;

	/// <summary>True when no game input should be accepted right now.</summary>
	public static bool Blocked
	{
		get
		{
			if (LocalPlayerFrozen) return true;
			if (SettingsMenu.IsAnyOpen) return true;
			if (ConsoleHud.IsAnyOpen) return true;
			if (DisplayServer.GetName() != "headless"
				&& !DisplayServer.WindowIsFocused(0))
				return true;
			// LocalPlayer dead → kein Movement/Fire/Aim. HP-Check via LastSelfSnap (authoritativ).
			// Bleibt geblockt bis Server-Respawn-Event Hp wieder > 0 setzt (siehe NetClient.HandleRespawn).
			var selfSnap = NetMain.Instance?.Client?.LastSelfSnap;
			if (selfSnap.HasValue && selfSnap.Value.Hp == 0) return true;
			return false;
		}
	}
}
