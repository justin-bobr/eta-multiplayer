using Godot;

public struct GrenadeInput
{
	/// <summary>True while the grenade slot is selected.</summary>
	public bool SlotActive;
	/// <summary>True while the throw button is held.</summary>
	public bool ThrowHeld;
	/// <summary>Tick delta time in seconds.</summary>
	public float Dt;
}

/// <summary>Pure-logic grenade charge: longer fire-hold = stronger throw (0..1). Deterministic, so identical
/// input streams yield identical <see cref="ThrownCharge"/> on client and server. Godot-independent (like
/// <see cref="MovementController"/>) so the server can replay it.</summary>
public class GrenadeController
{
	/// <summary>Tuning reference; defaults to <see cref="ConVars.Sv"/>.</summary>
	public SvConVars Sv = ConVars.Sv;

	public float Charge { get; private set; }
	public bool DidThrowThisFrame { get; private set; }
	public float ThrownCharge { get; private set; }

	private bool _wasHeld;

	/// <summary>Server-replayable step. Detects the release edge and triggers the throw.</summary>
	public void Step(GrenadeInput input)
	{
		DidThrowThisFrame = false;

		if (!input.SlotActive)
		{
			Charge = 0f;
			_wasHeld = false;
			return;
		}

		if (input.ThrowHeld)
		{
			Charge = Mathf.Min(1f, Charge + input.Dt / Mathf.Max(0.01f, Sv.GrenadeChargeToFull));
		}
		else if (_wasHeld)
		{
			DidThrowThisFrame = true;
			ThrownCharge = Mathf.Max(Sv.GrenadeMinCharge, Charge);
			Charge = 0f;
		}

		_wasHeld = input.ThrowHeld;
	}
}
