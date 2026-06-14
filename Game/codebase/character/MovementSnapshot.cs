using Godot;

namespace Vantix.Character;

/// <summary>
/// Complete <see cref="MovementController"/> state for client-side prediction reconciliation. Snapshotted
/// per tick into a ring buffer via <see cref="MovementController.Snapshot"/> and restored via
/// <see cref="MovementController.Restore"/> before replay. Value type (no GC). Excludes Sv (immutable) and
/// _fireRng (re-seeded deterministically from TickIndex+ShotIndex). Node-side state (transform, mantle) is
/// captured separately in the netcode snapshot.
/// </summary>
public struct MovementSnapshot
{
	public Vector3 Velocity;
	public float Stamina,
		CrouchBlend,
		StaminaRegenTimer;
	public bool SprintExhausted,
		SprintNeedsRelease;
	public int FireMode,
		ShotIndex;
	public float FireCooldown,
		TimeSinceLastShot,
		WeaponRaiseBlend,
		ReloadTimer,
		InspectTimer,
		AdsBlend;
	public bool FirePressedLast,
		ReloadPressedLast,
		InspectPressedLast;
	public Vector3 AimPunch;
	public int CurrentMag,
		ReserveAmmo,
		LastReloadMoved,
		PendingReloadIntoMag;
	public bool UnlimitedAmmo,
		ReloadWasActive;
	public float BreathHoldTimer,
		BreathRecoverTimer,
		BreathCooldownTimer;
	public bool BreathHoldActiveNow;
	public bool IsAirborne,
		CrouchCancelJumpUsed;
	public float PrevVelocityY,
		CoyoteTimer,
		JumpBufferTimer,
		TimeSinceJump,
		CrouchBufferTimer;
	public bool IsSliding;
	public float SlideTimer,
		SlideStopAccuracyTimer;
	public bool WallJumpAvailable,
		IsWallClinging;
	public float WallClingTimer,
		WallClingEntrySpeed,
		PreMoveHorizSpeed;
	public int WallClingChargesRemaining;
	public Vector3 LastWishDir,
		LastShotOrigin,
		LastShotDirection;
	public Vector2 LastShotPatternEntry;
	public float LastShotSpread;
	public bool ActuallySprinting,
		RecentlyFired,
		DidJumpThisFrame,
		DidWallJumpThisFrame,
		DidFireThisFrame,
		DidDryFireThisFrame,
		DidReloadThisFrame;
}
