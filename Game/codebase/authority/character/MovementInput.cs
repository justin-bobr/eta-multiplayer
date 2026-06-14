using Godot;

namespace Vantix.Character;

public struct MovementInput
{
	/// <summary>Sequence number used by replay and reconciliation.</summary>
	public uint TickIndex;
	public float Dt;

	/// <summary>Local-space input vector (X = strafe right positive, Z = back positive).</summary>
	public Vector3 WishDir;

	/// <summary>Body yaw in radians.</summary>
	public float ViewYaw;

	/// <summary>Head pitch in radians, used for the aim direction.</summary>
	public float ViewPitch;
	public bool SprintHeld;
	public bool ShiftHeld;
	public bool CrouchHeld;

	/// <summary>Press-edge of the crouch key — used to initiate slides.</summary>
	public bool CrouchPressed;

	/// <summary>Right-mouse hold. Blocks sprint and enables the ADS blend.</summary>
	public bool AdsHeld;

	/// <summary>Hold to dampen sway while in ADS for a few seconds before a shaky recover phase begins.</summary>
	public bool BreathHoldHeld;

	/// <summary>Press-edge of the jump key (not held). Used for regular jumps so bunny-hopping is impossible.</summary>
	public bool JumpPressed;

	/// <summary>Currently selected weapon — required for ADS speed multiplier and ADS blend time.</summary>
	public WeaponStats Weapon;

	/// <summary>Used for gravity and regular jumps. Server-derived physics truth.</summary>
	public bool OnFloor;

	/// <summary>Used for wall jumps. Server-derived physics truth.</summary>
	public bool TouchingWall;

	/// <summary>World-space wall normal. Server-derived physics truth.</summary>
	public Vector3 WallNormal;

	/// <summary>Subtick events ordered by <see cref="SubtickEvent.TFraction"/> ascending, or null/empty for
	/// the legacy single-segment path. See struct header for routing.</summary>
	public SubtickEvent[] Events;

	/// <summary>Held-input bitmask at the START of the tick (t=0). Used by the subtick path for the first
	/// segment before any event applies. Ignored on the legacy path.</summary>
	public InputBits InitialBits;

	/// <summary>View yaw at the start of the tick. Ignored on the legacy path.</summary>
	public float InitialViewYaw;

	/// <summary>View pitch at the start of the tick. Ignored on the legacy path.</summary>
	public float InitialViewPitch;

	/// <summary>Body basis derived from ViewYaw, used to transform WishDir into world space.</summary>
	public readonly Basis BodyBasis => Basis.FromEuler(new Vector3(0f, ViewYaw, 0f));

	/// <summary>Aim direction computed from ViewYaw and ViewPitch (unit forward vector used by server hitscan).</summary>
	public readonly Vector3 AimDirection
	{
		get
		{
			float cp = Mathf.Cos(ViewPitch);
			return new Vector3(-Mathf.Sin(ViewYaw) * cp, Mathf.Sin(ViewPitch), -Mathf.Cos(ViewYaw) * cp);
		}
	}
}
