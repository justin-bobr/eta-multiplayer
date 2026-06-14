using Godot;

namespace Vantix.Character;

public struct FireInput
{
	/// <summary>Sequence number — the server rewinds the world snapshot to this tick.</summary>
	public uint TickIndex;

	/// <summary>True from any source (Mouse1 or fire key).</summary>
	public bool FirePressed;

	/// <summary>Held state — MovementController detects the press edge itself.</summary>
	public bool ReloadPressed;

	/// <summary>Held state — MovementController detects the press edge itself.</summary>
	public bool InspectPressed;

	/// <summary>Held state for aim-down-sights.</summary>
	public bool AdsHeld;

	/// <summary>Gameplay flag (e.g. Dead).</summary>
	public bool CanFire;
	public WeaponStats Weapon;

	/// <summary>Horizontal speed for spread scaling.</summary>
	public float Speed;

	/// <summary>Shooter position at this tick — used by server-side lag compensation.</summary>
	public Vector3 ShooterPosition;

	/// <summary>Aim yaw — server raycast direction.</summary>
	public float ViewYaw;

	/// <summary>Aim pitch — server raycast direction.</summary>
	public float ViewPitch;
	public float Dt;

	/// <summary>Forward unit vector derived from yaw and pitch — the server raycasts from ShooterPosition along this.</summary>
	public readonly Vector3 AimDirection
	{
		get
		{
			float cp = Mathf.Cos(ViewPitch);
			return new Vector3(-Mathf.Sin(ViewYaw) * cp, Mathf.Sin(ViewPitch), -Mathf.Cos(ViewYaw) * cp);
		}
	}
}
