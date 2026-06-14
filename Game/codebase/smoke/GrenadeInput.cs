namespace Vantix.Smoke;

/// <summary>Per-tick grenade input (slot active, throw held, dt) for the charge logic.</summary>
public struct GrenadeInput
{
	/// <summary>True while the grenade slot is selected.</summary>
	public bool SlotActive;

	/// <summary>True while the throw button is held.</summary>
	public bool ThrowHeld;

	/// <summary>Tick delta time in seconds.</summary>
	public float Dt;
}
