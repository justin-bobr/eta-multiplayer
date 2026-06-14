using Godot;

/// <summary>Central input gate. <see cref="Blocked"/> is true when the player should receive no game input:
/// settings menu open, window unfocused (Alt-Tab), mouse capture off, or LocalPlayer dead. Input-read sites
/// should consult this first so keystrokes don't leak through.</summary>
public static class InputGate
{
	/// <summary>True from LocalPlayer._Ready until <c>WorldInitComplete</c> is sent; freezes input reads and
	/// SendNetInput so the player can't move or send pre-spawn ticks while preloads run.</summary>
	public static bool LocalPlayerFrozen;

	/// <summary>Cached once: DisplayServer.GetName() marshals a new managed string per call and Blocked is read
	/// every input-read site per tick, making it the largest steady allocation source in the client tick.</summary>
	private static readonly bool _headless = DisplayServer.GetName() == "headless";

	public static bool Blocked
	{
		get
		{
			if (LocalPlayerFrozen) return true;
			if (SettingsMenu.IsAnyOpen) return true;
			if (ConsoleHud.IsAnyOpen) return true;
			if (!_headless && !DisplayServer.WindowIsFocused(0))
				return true;
			var selfSnap = NetMain.Instance?.Client?.LastSelfSnap;
			if (selfSnap.HasValue && selfSnap.Value.Hp == 0) return true;
			return false;
		}
	}
}
