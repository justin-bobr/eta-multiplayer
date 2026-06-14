namespace Vantix.Smoke;

public struct GrenadeInput
{
	/// <summary>True while the grenade slot is selected.</summary>
	public bool SlotActive;

	/// <summary>True while the throw button is held.</summary>
	public bool ThrowHeld;

	/// <summary>Tick delta time in seconds.</summary>
	public float Dt;
}
