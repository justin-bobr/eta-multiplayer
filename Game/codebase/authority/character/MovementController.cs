using Godot;

/// <summary>
/// Per-tick input for the movement logic. Server-replayable.
///
/// NETCODE-SECURITY: <see cref="OnFloor"/>, <see cref="TouchingWall"/> and <see cref="WallNormal"/> are
/// physics-derived. They must NEVER be accepted from the client over the network or a cheat vector opens
/// (the client could claim OnFloor=true mid-air for infinite jumps, or TouchingWall=true in free space for
/// free wall jumps). The server must derive them from its own physics simulation. The client only sends
/// the user-intent fields (WishDir, *Held, *Pressed, View*).
/// </summary>
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

/// <summary>Per-tick input for the fire logic. Server-replayable including lag compensation.</summary>
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

/// <summary>
/// Complete <see cref="MovementController"/> state used for client-side prediction reconciliation.
/// Snapshotted per simulated tick into a ring buffer via <see cref="MovementController.Snapshot"/>. When a
/// server correction arrives the buffer entry for the corrected tick is loaded via
/// <see cref="MovementController.Restore"/> and the subsequent ticks are replayed. Value type so no GC
/// allocations occur during per-tick snapshotting.
///
/// Contains every mutable field of the controller (including private). NOT included:
///   - Sv (tuning config, immutable at runtime).
///   - _fireRng (re-seeded deterministically from TickIndex+ShotIndex on each shot inside DoFire — there is
///     no carry-over state, so ShotIndex alone is enough for replay).
/// The node-side state (CharacterBody3D transform, mantle lerp) must additionally be captured in the
/// netcode-layer snapshot — this struct covers only the pure-logic part.
/// </summary>
public struct MovementSnapshot
{
	public Vector3 Velocity;
	public float Stamina, CrouchBlend, StaminaRegenTimer;
	public bool SprintExhausted, SprintNeedsRelease;
	public int FireMode, ShotIndex;
	public float FireCooldown, TimeSinceLastShot, WeaponRaiseBlend, ReloadTimer, InspectTimer, AdsBlend;
	public bool FirePressedLast, ReloadPressedLast, InspectPressedLast;
	public Vector3 AimPunch;
	public int CurrentMag, ReserveAmmo, LastReloadMoved, PendingReloadIntoMag;
	public bool UnlimitedAmmo, ReloadWasActive;
	public float BreathHoldTimer, BreathRecoverTimer, BreathCooldownTimer;
	public bool BreathHoldActiveNow;
	public bool IsAirborne, CrouchCancelJumpUsed;
	public float PrevVelocityY, CoyoteTimer, JumpBufferTimer, TimeSinceJump, CrouchBufferTimer;
	public bool IsSliding;
	public float SlideTimer, SlideStopAccuracyTimer;
	public bool WallJumpAvailable, IsWallClinging;
	public float WallClingTimer, WallClingEntrySpeed, PreMoveHorizSpeed;
	public int WallClingChargesRemaining;
	public Vector3 LastWishDir, LastShotOrigin, LastShotDirection;
	public Vector2 LastShotPatternEntry;
	public float LastShotSpread;
	public bool ActuallySprinting, RecentlyFired, DidJumpThisFrame, DidWallJumpThisFrame, DidFireThisFrame, DidDryFireThisFrame;
}

/// <summary>
/// Pure movement logic. Tuning is injected via the <see cref="SvConVars"/> reference (default
/// = <see cref="ConVars.Sv"/>). Deterministic and free of Node3D / physics calls so it can run inside the
/// server tick and inside replay. Tests can swap <see cref="Sv"/> with a mock.
/// </summary>
public class MovementController
{
	/// <summary>Tuning reference. Default = global <see cref="ConVars.Sv"/>. Replaceable for tests.</summary>
	public SvConVars Sv = ConVars.Sv;

	public Vector3 Velocity;
	public float Stamina = 100f;
	public float CrouchBlend;
	public bool SprintExhausted;
	public bool SprintNeedsRelease;
	public float StaminaRegenTimer;

	/// <summary>0 = automatic, 1 = single-shot.</summary>
	public int FireMode;
	public float FireCooldown;
	public int ShotIndex;
	public bool FirePressedLast;
	public float TimeSinceLastShot = 999f;
	/// <summary>Gameplay aim shift (server-authoritative).</summary>
	public Vector3 AimPunch;
	/// <summary>1 = fully raised (fire-ready), 0 = fully lowered (sprint).</summary>
	public float WeaponRaiseBlend = 1f;
	/// <summary>Greater than zero means a reload is in progress and fire is blocked.</summary>
	public float ReloadTimer;
	public bool ReloadPressedLast;
	public bool IsReloading => ReloadTimer > 0f;

	public int CurrentMag;
	public int ReserveAmmo;
	/// <summary>Test mode: bullets are not decremented and reserves are refilled forever.</summary>
	public bool UnlimitedAmmo;
	private bool _reloadWasActive;
	private int _pendingReloadIntoMag;
	/// <summary>Public read-only — last reload bullet count moved (used by SFX/HUD/replay).</summary>
	public int LastReloadMoved { get; private set; }
	/// <summary>Greater than zero while the inspect animation is playing, ADS blocked.</summary>
	public float InspectTimer;
	public bool InspectPressedLast;
	public bool IsInspecting => InspectTimer > 0f;
	/// <summary>0 = hipfire, 1 = full ADS. Lerps with WeaponStats.AdsBlendTime.</summary>
	public float AdsBlend;

	/// <summary>Remaining hold stamina in seconds — drains during active hold, regenerates otherwise.</summary>
	public float BreathHoldTimer;
	/// <summary>Greater than zero while in the shaky recover phase (sway amplified).</summary>
	public float BreathRecoverTimer;
	/// <summary>Greater than zero during the post-recover cooldown when no new hold can be started.</summary>
	public float BreathCooldownTimer;
	/// <summary>Computed each tick: true while the hold is effectively active (drives the sway multiplier).</summary>
	public bool BreathHoldActiveNow;

	private bool _isAirborne;
	private float _prevVelocityY;
	/// <summary>Public read-only access for external systems (e.g. the mantle check in PlayerCore).</summary>
	public bool IsAirborne => _isAirborne;

	/// <summary>Horizontal speed captured before MoveAndSlide. Set by PlayerCore and used by the wall-jump
	/// check so that wall-absorbed velocity does not kill the speed gate.</summary>
	public float PreMoveHorizSpeed;

	private float _coyoteTimer;
	private float _jumpBufferTimer;
	private float _crouchBufferTimer;
	private bool _jumpAwaitingCrouchBoost;

	private float _timeSinceJump = 999f;
	private bool _crouchCancelJumpUsed;

	public bool IsSliding;
	public float SlideTimer;
	/// <summary>Greater than zero while still inside the slide-stop accuracy window. The first shot fired in
	/// this window benefits from the SlideStopAccuracySpreadMul multiplier.</summary>
	public float SlideStopAccuracyTimer;

	/// <summary>Smoothstepped variant of AdsBlend for visual use (pose, FOV, sensitivity). Gameplay code uses the linear AdsBlend.</summary>
	public float AdsBlendVisual => AdsBlend * AdsBlend * (3f - 2f * AdsBlend);

	/// <summary>One wall jump per airtime, reset on landing.</summary>
	public bool WallJumpAvailable = true;

	public bool IsWallClinging;
	public float WallClingTimer;
	/// <summary>Negative one means uninitialized — lazy-initialized from Sv.WallClingChargesPerSpawn on first tick.</summary>
	public int WallClingChargesRemaining = -1;
	/// <summary>Horizontal speed at cling entry, saved so the cling-exit jump can bypass the wall-jump speed floor.</summary>
	public float WallClingEntrySpeed;

	/// <summary>Resets per-spawn consumables (wall-cling charges, breath-hold stamina, etc.). Called by PlayerCore on respawn.</summary>
	public void ResetSpawnConsumables()
	{
		WallClingChargesRemaining = Sv.WallClingChargesPerSpawn;
		IsWallClinging = false;
		WallClingTimer = 0f;
		WallClingEntrySpeed = 0f;
		BreathHoldTimer = Sv.BreathHoldDuration;
		BreathRecoverTimer = 0f;
		BreathCooldownTimer = 0f;
		BreathHoldActiveNow = false;
	}

	/// <summary>Initializes ammo from weapon stats (full mag plus full reserve). Called by PlayerCore on
	/// spawn and on weapon switch. Server-authoritative — the client replicates this.</summary>
	public void InitializeAmmo(WeaponStats weapon)
	{
		if (weapon == null) { CurrentMag = 0; ReserveAmmo = 0; return; }
		CurrentMag = weapon.MagazineSize;
		ReserveAmmo = weapon.MaxReserveAmmo;
		UnlimitedAmmo = Sv.UnlimitedAmmoDefault;
		ReloadTimer = 0f;
		_reloadWasActive = false;
		_pendingReloadIntoMag = 0;
		LastReloadMoved = 0;
	}

	/// <summary>Builds a full state snapshot (see <see cref="MovementSnapshot"/>). Value-type return — no
	/// allocation, safe to call every tick.</summary>
	public MovementSnapshot Snapshot() => new()
	{
		Velocity = Velocity,
		Stamina = Stamina,
		CrouchBlend = CrouchBlend,
		StaminaRegenTimer = StaminaRegenTimer,
		SprintExhausted = SprintExhausted,
		SprintNeedsRelease = SprintNeedsRelease,
		FireMode = FireMode,
		ShotIndex = ShotIndex,
		FireCooldown = FireCooldown,
		TimeSinceLastShot = TimeSinceLastShot,
		WeaponRaiseBlend = WeaponRaiseBlend,
		ReloadTimer = ReloadTimer,
		InspectTimer = InspectTimer,
		AdsBlend = AdsBlend,
		FirePressedLast = FirePressedLast,
		ReloadPressedLast = ReloadPressedLast,
		InspectPressedLast = InspectPressedLast,
		AimPunch = AimPunch,
		CurrentMag = CurrentMag,
		ReserveAmmo = ReserveAmmo,
		LastReloadMoved = LastReloadMoved,
		PendingReloadIntoMag = _pendingReloadIntoMag,
		UnlimitedAmmo = UnlimitedAmmo,
		ReloadWasActive = _reloadWasActive,
		BreathHoldTimer = BreathHoldTimer,
		BreathRecoverTimer = BreathRecoverTimer,
		BreathCooldownTimer = BreathCooldownTimer,
		BreathHoldActiveNow = BreathHoldActiveNow,
		IsAirborne = _isAirborne,
		CrouchCancelJumpUsed = _crouchCancelJumpUsed,
		PrevVelocityY = _prevVelocityY,
		CoyoteTimer = _coyoteTimer,
		JumpBufferTimer = _jumpBufferTimer,
		TimeSinceJump = _timeSinceJump,
		CrouchBufferTimer = _crouchBufferTimer,
		IsSliding = IsSliding,
		SlideTimer = SlideTimer,
		SlideStopAccuracyTimer = SlideStopAccuracyTimer,
		WallJumpAvailable = WallJumpAvailable,
		IsWallClinging = IsWallClinging,
		WallClingTimer = WallClingTimer,
		WallClingEntrySpeed = WallClingEntrySpeed,
		PreMoveHorizSpeed = PreMoveHorizSpeed,
		WallClingChargesRemaining = WallClingChargesRemaining,
		LastWishDir = LastWishDir,
		LastShotOrigin = LastShotOrigin,
		LastShotDirection = LastShotDirection,
		LastShotPatternEntry = LastShotPatternEntry,
		LastShotSpread = LastShotSpread,
		ActuallySprinting = ActuallySprinting,
		RecentlyFired = RecentlyFired,
		DidJumpThisFrame = DidJumpThisFrame,
		DidWallJumpThisFrame = DidWallJumpThisFrame,
		DidFireThisFrame = DidFireThisFrame,
		DidDryFireThisFrame = DidDryFireThisFrame,
	};

	/// <summary>Restores the full state from a <see cref="MovementSnapshot"/> (reconciliation rollback). The
	/// caller then replays the ticks after the snapshot.</summary>
	public void Restore(in MovementSnapshot s)
	{
		Velocity = s.Velocity;
		Stamina = s.Stamina;
		CrouchBlend = s.CrouchBlend;
		StaminaRegenTimer = s.StaminaRegenTimer;
		SprintExhausted = s.SprintExhausted;
		SprintNeedsRelease = s.SprintNeedsRelease;
		FireMode = s.FireMode;
		ShotIndex = s.ShotIndex;
		FireCooldown = s.FireCooldown;
		TimeSinceLastShot = s.TimeSinceLastShot;
		WeaponRaiseBlend = s.WeaponRaiseBlend;
		ReloadTimer = s.ReloadTimer;
		InspectTimer = s.InspectTimer;
		AdsBlend = s.AdsBlend;
		FirePressedLast = s.FirePressedLast;
		ReloadPressedLast = s.ReloadPressedLast;
		InspectPressedLast = s.InspectPressedLast;
		AimPunch = s.AimPunch;
		CurrentMag = s.CurrentMag;
		ReserveAmmo = s.ReserveAmmo;
		LastReloadMoved = s.LastReloadMoved;
		_pendingReloadIntoMag = s.PendingReloadIntoMag;
		UnlimitedAmmo = s.UnlimitedAmmo;
		_reloadWasActive = s.ReloadWasActive;
		BreathHoldTimer = s.BreathHoldTimer;
		BreathRecoverTimer = s.BreathRecoverTimer;
		BreathCooldownTimer = s.BreathCooldownTimer;
		BreathHoldActiveNow = s.BreathHoldActiveNow;
		_isAirborne = s.IsAirborne;
		_crouchCancelJumpUsed = s.CrouchCancelJumpUsed;
		_prevVelocityY = s.PrevVelocityY;
		_coyoteTimer = s.CoyoteTimer;
		_jumpBufferTimer = s.JumpBufferTimer;
		_timeSinceJump = s.TimeSinceJump;
		_crouchBufferTimer = s.CrouchBufferTimer;
		IsSliding = s.IsSliding;
		SlideTimer = s.SlideTimer;
		SlideStopAccuracyTimer = s.SlideStopAccuracyTimer;
		WallJumpAvailable = s.WallJumpAvailable;
		IsWallClinging = s.IsWallClinging;
		WallClingTimer = s.WallClingTimer;
		WallClingEntrySpeed = s.WallClingEntrySpeed;
		PreMoveHorizSpeed = s.PreMoveHorizSpeed;
		WallClingChargesRemaining = s.WallClingChargesRemaining;
		LastWishDir = s.LastWishDir;
		LastShotOrigin = s.LastShotOrigin;
		LastShotDirection = s.LastShotDirection;
		LastShotPatternEntry = s.LastShotPatternEntry;
		LastShotSpread = s.LastShotSpread;
		ActuallySprinting = s.ActuallySprinting;
		RecentlyFired = s.RecentlyFired;
		DidJumpThisFrame = s.DidJumpThisFrame;
		DidWallJumpThisFrame = s.DidWallJumpThisFrame;
		DidFireThisFrame = s.DidFireThisFrame;
		DidDryFireThisFrame = s.DidDryFireThisFrame;
	}

	/// <summary>
	/// Sway multiplier for mouse inertia / lean / bob / velocity tilt (everything except breathing). 1.0 =
	/// neutral, &lt;1 = reduced (active hold, sharp aim), &gt;1 = amplified (recover phase, hands shaky).
	/// </summary>
	public float BreathSwayMul
	{
		get
		{
			if (!Sv.BreathHoldEnabled) return 1f;
			if (BreathRecoverTimer > 0f) return Sv.BreathHoldShakySwayMul;
			if (BreathHoldActiveNow) return Sv.BreathHoldSwayMul;
			return 1f;
		}
	}

	/// <summary>
	/// Separate multiplier for the breathing oscillation only — intentionally less damped during a hold than
	/// the rest (so residual breath movement remains visible, never fully sterile). In recover it is
	/// amplified to convey the hectic state.
	/// </summary>
	public float BreathBreathingMul
	{
		get
		{
			if (!Sv.BreathHoldEnabled) return 1f;
			if (BreathRecoverTimer > 0f) return Sv.BreathHoldShakyBreathingMul;
			if (BreathHoldActiveNow) return Sv.BreathHoldBreathingMul;
			return 1f;
		}
	}

	/// <summary>Last frame's body-local WishDir (used by the mantle intent check).</summary>
	public Vector3 LastWishDir { get; private set; }
	public bool ActuallySprinting { get; private set; }
	public bool DidJumpThisFrame { get; private set; }
	public bool DidWallJumpThisFrame { get; private set; }
	public bool DidFireThisFrame { get; private set; }
	/// <summary>True on the tick the player clicked on an empty magazine (dry-fire). One-tick edge.</summary>
	public bool DidDryFireThisFrame { get; private set; }
	/// <summary>World origin of the last shot (server truth, lag-comp replayable). Camera/eye position.</summary>
	public Vector3 LastShotOrigin { get; private set; }
	/// <summary>World direction of the last shot including aim punch, pattern and spread. Unit vector.</summary>
	public Vector3 LastShotDirection { get; private set; }
	public Vector2 LastShotPatternEntry { get; private set; }
	public float LastShotSpread { get; private set; }
	public bool RecentlyFired { get; private set; }
	/// <summary>Horizontal speed magnitude (X and Z components only). Inlined sqrt — avoids the per-call Vector3 ctor when this property is read in 128 Hz hot paths (footstep/fire/anim).</summary>
	public float HorizontalSpeed => Mathf.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);

	private readonly Godot.RandomNumberGenerator _fireRng = new();

	/// <summary>Server-replayable fire step. Updates cooldown, ShotIndex, AimPunch and the computed outputs.</summary>
	public void FireStep(FireInput input)
	{
		DidFireThisFrame = false;
		DidDryFireThisFrame = false;
		FireCooldown = Mathf.Max(0f, FireCooldown - input.Dt);
		ReloadTimer = Mathf.Max(0f, ReloadTimer - input.Dt);
		InspectTimer = Mathf.Max(0f, InspectTimer - input.Dt);
		SlideStopAccuracyTimer = Mathf.Max(0f, SlideStopAccuracyTimer - input.Dt);

		bool reloadNowActive = ReloadTimer > 0f;
		if (_reloadWasActive && !reloadNowActive)
		{
			int moved = Mathf.Min(_pendingReloadIntoMag, ReserveAmmo);
			CurrentMag += moved;
			ReserveAmmo -= moved;
			LastReloadMoved = moved;
			_pendingReloadIntoMag = 0;
		}
		_reloadWasActive = reloadNowActive;

		if (UnlimitedAmmo && ReserveAmmo <= 0 && input.Weapon != null)
			ReserveAmmo = input.Weapon.MaxReserveAmmo;

		bool reloadEdge = input.ReloadPressed && !ReloadPressedLast;
		ReloadPressedLast = input.ReloadPressed;
		if (reloadEdge && !IsReloading && input.Weapon != null)
		{
			int magSize = input.Weapon.MagazineSize;
			int needed = magSize - CurrentMag;
			bool hasReserveOrUnlimited = UnlimitedAmmo || ReserveAmmo > 0;
			if (needed > 0 && hasReserveOrUnlimited)
			{
				ReloadTimer = input.Weapon.ReloadTime;
				InspectTimer = 0f;
				_pendingReloadIntoMag = needed;
			}
		}

		bool inspectEdge = input.InspectPressed && !InspectPressedLast;
		InspectPressedLast = input.InspectPressed;
		if (inspectEdge && !IsReloading && !IsInspecting && input.Weapon != null)
			InspectTimer = input.Weapon.InspectTime;

		bool wantsFire = FireMode == 0 ? input.FirePressed : (input.FirePressed && !FirePressedLast);
		FirePressedLast = input.FirePressed;

		bool weaponReady = WeaponRaiseBlend >= Sv.SprintFireGateBlend;
		bool hasAmmo = CurrentMag > 0;

		if (input.CanFire && weaponReady && !IsReloading && hasAmmo && wantsFire && FireCooldown <= 0f && input.Weapon != null)
		{
			FireCooldown = 1f / Mathf.Max(0.1f, input.Weapon.FireRate);
			InspectTimer = 0f;
			CurrentMag = Mathf.Max(0, CurrentMag - 1);
			DoFire(input);
			TimeSinceLastShot = 0f;
		}
		else if (input.CanFire && weaponReady && !IsReloading && !hasAmmo && wantsFire && FireCooldown <= 0f && input.Weapon != null)
		{
			DidDryFireThisFrame = true;
			FireCooldown = 1f / Mathf.Max(0.1f, input.Weapon.FireRate);
		}

		TimeSinceLastShot += input.Dt;
		float resetDelay = input.Weapon?.PatternResetDelay ?? 0.35f;
		if (TimeSinceLastShot >= resetDelay) ShotIndex = 0;
		RecentlyFired = TimeSinceLastShot < 1.5f / Mathf.Max(0.1f, input.Weapon?.FireRate ?? 10f);

		float aimRec = RecentlyFired ? (input.Weapon?.AimPunchRecoveryFiring ?? 3f) : (input.Weapon?.AimPunchRecoveryReleased ?? 18f);
		AimPunch = AimPunch.Lerp(Vector3.Zero, Mathf.Min(1f, aimRec * input.Dt));
	}

	/// <summary>Performs the actual shot: computes pattern + spread + bloom and updates AimPunch and the
	/// LastShot* outputs.</summary>
	private void DoFire(FireInput input)
	{
		var w = input.Weapon;
		var pattern = w.RecoilPattern;
		Vector2 p = Vector2.Zero;
		if (pattern != null && pattern.Length > 0)
		{
			int idx = Mathf.Min(ShotIndex, pattern.Length - 1);
			p = pattern[idx] * w.PatternScale;
		}
		ShotIndex++;
		LastShotPatternEntry = p;

		_fireRng.Seed = ((ulong)input.TickIndex * 2654435761u) ^ ((ulong)ShotIndex * 40503u);

		float speed = input.Speed;
		float movementSpread;
		if (speed < 0.05f) movementSpread = 0f;
		else if (speed <= Sv.ShiftSpeed + 0.1f)
			movementSpread = Mathf.Lerp(0f, w.MovementSpreadShift, speed / Mathf.Max(0.01f, Sv.ShiftSpeed));
		else if (speed <= Sv.WalkSpeed + 0.1f)
			movementSpread = Mathf.Lerp(w.MovementSpreadShift, w.MovementSpreadWalk, (speed - Sv.ShiftSpeed) / Mathf.Max(0.01f, Sv.WalkSpeed - Sv.ShiftSpeed));
		else
			movementSpread = Mathf.Lerp(w.MovementSpreadWalk, w.MovementSpread, Mathf.Clamp((speed - Sv.WalkSpeed) / Mathf.Max(0.01f, Sv.SprintSpeed - Sv.WalkSpeed), 0f, 1f));
		movementSpread *= Mathf.Lerp(1f, w.AdsMovementSpreadMul, AdsBlend);
		float bloomT = Mathf.Pow(Mathf.Clamp(ShotIndex / Mathf.Max(1f, w.HipfireBloomShots), 0f, 1f), w.HipfireBloomCurve);
		float bloomScale = Mathf.Lerp(1f, w.AdsBloomMul, AdsBlend);
		float bloomSpread = w.HipfireBloomMax * bloomT * bloomScale;
		float spreadMag = w.HipfireBaseSpread + movementSpread + bloomSpread;
		float adsTarget = _isAirborne ? w.AdsSpreadAirMul : w.AdsSpreadMul;
		spreadMag *= Mathf.Lerp(1f, adsTarget, AdsBlend);
		if (_isAirborne) spreadMag *= w.AirborneSpreadMul;
		if (SlideStopAccuracyTimer > 0f && Sv.SlideStopAccuracyEnabled)
		{
			spreadMag *= Sv.SlideStopAccuracySpreadMul;
			SlideStopAccuracyTimer = 0f;
		}
		if (Sv.BreathHoldEnabled)
		{
			if (BreathRecoverTimer > 0f) spreadMag *= Sv.BreathHoldShakySpreadMul;
			else if (BreathHoldActiveNow) spreadMag *= Sv.BreathHoldSpreadMul;
		}
		LastShotSpread = spreadMag;

		float spreadPitch = _fireRng.RandfRange(-1f, 1f) * spreadMag;
		float spreadYaw = _fireRng.RandfRange(-1f, 1f) * spreadMag;
		AimPunch += new Vector3(-(p.Y + spreadPitch), -p.X + spreadYaw, 0f);
		AimPunch.X = Mathf.Clamp(AimPunch.X, -w.AimPunchMaxClimb, w.AimPunchMaxClimb * 0.2f);
		AimPunch.Y = Mathf.Clamp(AimPunch.Y, -w.AimPunchMaxClimb * 0.8f, w.AimPunchMaxClimb * 0.8f);

		float effYaw = input.ViewYaw + Mathf.DegToRad(AimPunch.Y);
		float effPitch = Mathf.Clamp(input.ViewPitch - Mathf.DegToRad(AimPunch.X), -1.4f, 1.4f);
		float cp = Mathf.Cos(effPitch);
		LastShotDirection = new Vector3(-Mathf.Sin(effYaw) * cp, Mathf.Sin(effPitch), -Mathf.Cos(effYaw) * cp);
		LastShotOrigin = input.ShooterPosition;

		DidFireThisFrame = true;
	}

	/// <summary>Server-replayable movement step. Updates velocity from the input including jump, gravity,
	/// slide, crouch blend, stamina, ADS blend, breath hold and horizontal acceleration.</summary>
	public void Step(MovementInput input)
	{
		DidJumpThisFrame = false;
		DidWallJumpThisFrame = false;
		LastWishDir = input.WishDir;
		float dt = input.Dt;
		Vector3 velocity = Velocity;

		if (input.OnFloor) WallJumpAvailable = true;

		_timeSinceJump = input.OnFloor ? 999f : _timeSinceJump + dt;
		if (input.OnFloor) _crouchCancelJumpUsed = false;

		UpdateWallCling(ref velocity, input, dt);

		if (!IsWallClinging)
			ApplyGravity(ref velocity, input.OnFloor, dt);
		TryJump(ref velocity, input);
		TryCrouchCancelJump(ref velocity, input);
		UpdateCrouchBlend(input.CrouchHeld, dt);
		UpdateStamina(input);
		UpdateWeaponRaiseBlend(dt);
		UpdateAdsBlend(input, velocity, dt);
		UpdateBreathHold(input, dt);
		UpdateSlide(ref velocity, input, dt);
		float targetSpeed = ComputeTargetSpeed(input);
		ApplyHorizontalMovement(ref velocity, input, targetSpeed, dt);

		Velocity = velocity;
	}

	/// <summary>Applies gravity to vertical velocity. Includes an apex-hang reduction for floaty jumps.</summary>
	private void ApplyGravity(ref Vector3 velocity, bool onFloor, float dt)
	{
		if (onFloor) return;
		float g = Sv.Gravity;
		if (Mathf.Abs(velocity.Y) < Sv.ApexHangThreshold) g *= Sv.ApexHangGravityMul;
		velocity.Y -= g * dt;
	}

	/// <summary>Handles regular jumps, wall jumps, and the coyote-time / jump-buffer / crouch-buffer windows.</summary>
	private void TryJump(ref Vector3 velocity, MovementInput input)
	{
		float horizSpeed = new Vector3(velocity.X, 0f, velocity.Z).Length();

		if (!_isAirborne) _coyoteTimer = 0f; else _coyoteTimer += input.Dt;
		if (input.JumpPressed) _jumpBufferTimer = Sv.JumpBufferTime;
		else _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - input.Dt);
		if (input.CrouchPressed) _crouchBufferTimer = Sv.CrouchJumpBufferTime;
		else _crouchBufferTimer = Mathf.Max(0f, _crouchBufferTimer - input.Dt);

		if (_jumpAwaitingCrouchBoost && input.CrouchPressed && velocity.Y > 0f
			&& _timeSinceJump < Sv.CrouchJumpBufferTime
			&& input.WishDir.LengthSquared() > 0.01f)
		{
			velocity.Y += Sv.CrouchJumpBonus;
			_jumpAwaitingCrouchBoost = false;
		}
		if (_jumpAwaitingCrouchBoost && (_timeSinceJump >= Sv.CrouchJumpBufferTime || velocity.Y <= 0f))
			_jumpAwaitingCrouchBoost = false;

		bool wantsJump = _jumpBufferTimer > 0f;
		bool grounded = !_isAirborne || _coyoteTimer <= Sv.CoyoteTime;

		if (wantsJump && grounded)
		{
			float speedT = horizSpeed > Sv.JumpSpeedBonusThreshold
				? Mathf.Clamp((horizSpeed - Sv.JumpSpeedBonusThreshold) / Mathf.Max(0.01f, Sv.SprintSpeed - Sv.JumpSpeedBonusThreshold), 0f, 1f)
				: 0f;
			bool hasMovementInput = input.WishDir.LengthSquared() > 0.01f;
			bool crouchForJump = input.CrouchHeld || _crouchBufferTimer > 0f;
			float crouchBonusT = hasMovementInput ? (crouchForJump ? 1f : CrouchBlend) : 0f;
			velocity.Y = Sv.JumpVelocity + speedT * Sv.JumpSpeedBonus + Sv.CrouchJumpBonus * crouchBonusT;

			if (input.TouchingWall && input.WishDir.LengthSquared() > 0.01f)
			{
				Vector3 worldDir = input.BodyBasis * input.WishDir.Normalized();
				float intoWall = worldDir.Dot(-input.WallNormal);
				if (intoWall > 0.5f)
					velocity.Y += Sv.WallAssistBonus;
			}

			bool wantsBoost = input.WishDir.LengthSquared() > 0.01f && horizSpeed < Sv.WalkSpeed * 0.7f;
			if (wantsBoost)
			{
				Vector3 worldDir = input.BodyBasis * input.WishDir.Normalized();
				velocity.X += worldDir.X * Sv.JumpForwardBoost;
				velocity.Z += worldDir.Z * Sv.JumpForwardBoost;
			}

			if (ActuallySprinting && input.WishDir.LengthSquared() > 0.01f)
			{
				Vector3 worldDir = input.BodyBasis * input.WishDir.Normalized();
				velocity.X += worldDir.X * Sv.JumpSprintForwardBoost;
				velocity.Z += worldDir.Z * Sv.JumpSprintForwardBoost;
			}

			DidJumpThisFrame = true;
			IsSliding = false;
			_jumpBufferTimer = 0f;
			_coyoteTimer = Sv.CoyoteTime + 1f;
			_timeSinceJump = 0f;
			_crouchCancelJumpUsed = false;
			_jumpAwaitingCrouchBoost = crouchBonusT < 0.99f && hasMovementInput;
		}
		else if (!input.OnFloor && input.TouchingWall && WallJumpAvailable)
		{
			float effHorizSpeed = Mathf.Max(horizSpeed, Mathf.Max(PreMoveHorizSpeed, IsWallClinging ? WallClingEntrySpeed : 0f));
			if (effHorizSpeed >= Sv.WallJumpMinSpeed)
			{
				float speedFactor = Mathf.Clamp(effHorizSpeed / Mathf.Max(0.01f, Sv.WallJumpSpeedRef), 0.6f, 1.1f);
				Vector3 wallH = new Vector3(input.WallNormal.X, 0f, input.WallNormal.Z);
				if (wallH.LengthSquared() > 0.0001f) wallH = wallH.Normalized();
				else wallH = Vector3.Forward;

				Vector3 lookDir = new Vector3(-Mathf.Sin(input.ViewYaw), 0f, -Mathf.Cos(input.ViewYaw));
				float lookIntoWall = -lookDir.Dot(wallH);
				if (lookIntoWall > 0f)
					lookDir = (lookDir + 2f * lookIntoWall * wallH).Normalized();

				float lw = Sv.WallJumpLookWeight;
				Vector3 jumpDir = lookDir * lw + wallH * (1f - lw);
				if (jumpDir.LengthSquared() > 0.0001f) jumpDir = jumpDir.Normalized();
				else jumpDir = wallH;

				float outSpeed = effHorizSpeed * Sv.WallJumpMomentumKeep + Sv.WallJumpHorizontal * speedFactor;
				velocity.X = jumpDir.X * outSpeed;
				velocity.Z = jumpDir.Z * outSpeed;

				velocity.Y = Mathf.Max(velocity.Y, 0f) + Sv.WallJumpVertical * speedFactor;

				WallJumpAvailable = false;
				DidWallJumpThisFrame = true;
				IsWallClinging = false;
				WallClingTimer = 0f;
			}
		}
	}

	/// <summary>Wall-cling state machine. Freezes the player against a wall after a sprint+jump approach;
	/// a re-jump triggers a regular wall jump with the saved entry speed bypassing the speed floor. Limited
	/// per spawn by <see cref="WallClingChargesRemaining"/>.</summary>
	private void UpdateWallCling(ref Vector3 velocity, MovementInput input, float dt)
	{
		if (!Sv.WallClingEnabled) return;

		if (WallClingChargesRemaining < 0) WallClingChargesRemaining = Sv.WallClingChargesPerSpawn;

		if (IsWallClinging)
		{
			WallClingTimer -= dt;
			if (WallClingTimer <= 0f || !input.TouchingWall || !_isAirborne)
			{
				IsWallClinging = false;
				return;
			}
			velocity.X = 0f;
			velocity.Y = 0f;
			velocity.Z = 0f;
			return;
		}

		if (WallClingChargesRemaining <= 0) return;
		if (!_isAirborne) return;
		if (!input.TouchingWall) return;
		if (!ActuallySprinting) return;
		if (_timeSinceJump < Sv.WallClingPostJumpGrace) return;
		if (input.WishDir.LengthSquared() < 0.01f) return;
		if (DidWallJumpThisFrame) return;

		Vector3 worldDir = input.BodyBasis * input.WishDir.Normalized();
		float intoWall = worldDir.Dot(-input.WallNormal);
		if (intoWall < Sv.WallClingIntoWallDot) return;

		float horizSpeed = new Vector3(velocity.X, 0f, velocity.Z).Length();
		float entrySpeed = Mathf.Max(horizSpeed, PreMoveHorizSpeed);
		if (entrySpeed < Sv.WallClingMinSpeed) return;

		IsWallClinging = true;
		WallClingTimer = Sv.WallClingDuration;
		WallClingEntrySpeed = entrySpeed;
		WallClingChargesRemaining--;
		velocity.X = 0f;
		velocity.Y = 0f;
		velocity.Z = 0f;
	}

	/// <summary>Crouch-cancel-jump skill tech: a precise Ctrl press inside the apex window grants a small
	/// vertical boost for an extra few centimetres of reach. The window is narrow (60-180 ms after the
	/// jump) so the technique is pros-only. One-shot per airtime, reset on landing or a new jump.</summary>
	private void TryCrouchCancelJump(ref Vector3 velocity, MovementInput input)
	{
		if (!Sv.CrouchCancelJumpEnabled) return;
		if (_crouchCancelJumpUsed) return;
		if (input.OnFloor) return;
		if (!input.CrouchPressed) return;
		if (_timeSinceJump < Sv.CrouchCancelJumpWindowStart) return;
		if (_timeSinceJump > Sv.CrouchCancelJumpWindowEnd) return;
		if (velocity.Y < 0f) return;

		velocity.Y += Sv.CrouchCancelJumpBonus;
		_crouchCancelJumpUsed = true;
	}

	/// <summary>Moves the crouch blend toward 1 (crouched) or 0 (standing).</summary>
	private void UpdateCrouchBlend(bool crouchHeld, float dt)
	{
		CrouchBlend = Mathf.MoveToward(CrouchBlend, crouchHeld ? 1f : 0f, Sv.CrouchTransitionSpeed * dt);
	}

	/// <summary>Lerps the weapon raise blend toward 0 while sprinting (lowered) or 1 otherwise. Reload
	/// overrides sprint-lower so the weapon stays raised while reloading.</summary>
	private void UpdateWeaponRaiseBlend(float dt)
	{
		bool lower = ActuallySprinting && !IsReloading;
		float target = lower ? 0f : 1f;
		float time = lower ? Sv.SprintLowerTime : Sv.SprintRaiseTime;
		float rate = 1f / Mathf.Max(0.01f, time);
		WeaponRaiseBlend = Mathf.MoveToward(WeaponRaiseBlend, target, rate * dt);
	}

	/// <summary>Slide state machine. Initiate on crouch-press during a fast sprint, apply friction over time,
	/// end on slow speed, timeout, crouch release, or air; open the slide-stop accuracy window on end.</summary>
	private void UpdateSlide(ref Vector3 velocity, MovementInput input, float dt)
	{
		if (!Sv.SlideEnabled) { IsSliding = false; return; }

		if (!IsSliding && input.CrouchPressed && !DidJumpThisFrame && ActuallySprinting && input.OnFloor)
		{
			float speed = new Vector3(velocity.X, 0f, velocity.Z).Length();
			if (speed >= Sv.SlideStartSpeedMin)
			{
				IsSliding = true;
				SlideTimer = 0f;
				Vector3 horiz = new Vector3(velocity.X, 0f, velocity.Z);
				if (horiz.LengthSquared() > 0.0001f)
				{
					Vector3 boost = horiz.Normalized() * Sv.SlideBoostSpeed;
					velocity.X = boost.X;
					velocity.Z = boost.Z;
				}
			}
		}

		if (IsSliding)
		{
			SlideTimer += dt;
			Vector3 horiz = new Vector3(velocity.X, 0f, velocity.Z);
			horiz = horiz.MoveToward(Vector3.Zero, Sv.SlideFriction * dt);
			velocity.X = horiz.X;
			velocity.Z = horiz.Z;
			float curSpeed = horiz.Length();
			if (curSpeed < Sv.SlideMinSpeed || SlideTimer >= Sv.SlideMaxTime
				|| !input.CrouchHeld || !input.OnFloor)
			{
				IsSliding = false;
				if (Sv.SlideStopAccuracyEnabled)
				{
					SlideStopAccuracyTimer = Sv.SlideStopAccuracyWindow;
					if (Sv.SlideStopHardBrake && input.OnFloor)
					{
						velocity.X = 0f;
						velocity.Z = 0f;
					}
				}
			}
		}
	}

	/// <summary>Breath-hold three-phase state machine (hold → recover → cooldown → idle/regen). Initial
	/// values come from <see cref="ResetSpawnConsumables"/>.</summary>
	private void UpdateBreathHold(MovementInput input, float dt)
	{
		BreathHoldActiveNow = false;
		if (!Sv.BreathHoldEnabled) return;

		BreathCooldownTimer = Mathf.Max(0f, BreathCooldownTimer - dt);

		if (BreathRecoverTimer > 0f)
		{
			BreathRecoverTimer -= dt;
			if (BreathRecoverTimer <= 0f)
			{
				BreathRecoverTimer = 0f;
				BreathCooldownTimer = Sv.BreathHoldCooldownAfterRecover;
				BreathHoldTimer = Sv.BreathHoldDuration;
			}
			return;
		}

		bool inAds = AdsBlend > 0.5f;
		if (input.BreathHoldHeld && inAds && BreathHoldTimer > 0f && BreathCooldownTimer <= 0f)
		{
			BreathHoldActiveNow = true;
			BreathHoldTimer -= dt;
			if (BreathHoldTimer <= 0f)
			{
				BreathHoldTimer = 0f;
				BreathRecoverTimer = Sv.BreathHoldRecoverDuration;
			}
		}
		else
		{
			BreathHoldTimer = Mathf.Min(Sv.BreathHoldDuration, BreathHoldTimer + dt * Sv.BreathHoldDuration * 0.5f);
		}
	}

	/// <summary>Tracks the airborne state machine (server-derived) and lerps the ADS blend toward the
	/// gameplay target with the per-weapon blend time. Airborne is now driven by <see cref="MovementInput.OnFloor"/>
	/// (deterministic, server-authoritative) instead of pure velocity heuristics — the old logic could get
	/// stuck in "airborne" on bumps because a short positive Y spike set the flag without ever clearing it.</summary>
	private void UpdateAdsBlend(MovementInput input, Vector3 velocity, float dt)
	{
		if (DidJumpThisFrame) _isAirborne = true;
		else _isAirborne = !input.OnFloor;
		_prevVelocityY = velocity.Y;

		bool adsAllowed = input.AdsHeld && !ActuallySprinting && !IsSliding && !IsReloading && !IsInspecting && WeaponRaiseBlend >= 0.95f;
		float target = adsAllowed ? 1f : 0f;
		float blendTime = input.Weapon?.AdsBlendTime ?? 0.18f;
		float rate = 1f / Mathf.Max(0.01f, blendTime);
		AdsBlend = Mathf.MoveToward(AdsBlend, target, rate * dt);
	}

	/// <summary>Drives sprint/stamina state including drain, regen, exhaustion and auto-resume.</summary>
	private void UpdateStamina(MovementInput input)
	{
		bool hasInput = input.WishDir.LengthSquared() > 0.01f;
		bool sprintInput = input.SprintHeld && hasInput && !input.CrouchHeld && !input.AdsHeld;

		bool justExhausted = !SprintExhausted && Stamina <= 0f;
		if (Stamina <= 0f)
		{
			SprintExhausted = true;
			SprintNeedsRelease = true;
		}
		if (justExhausted) StaminaRegenTimer = Sv.StaminaExhaustTimeout;
		if (SprintExhausted && Stamina >= Sv.StaminaSprintThreshold)
		{
			SprintExhausted = false;
			SprintNeedsRelease = false;
		}
		if (!input.SprintHeld) SprintNeedsRelease = false;

		ActuallySprinting = sprintInput && !SprintExhausted && !SprintNeedsRelease && Stamina > 0f;

		float dt = input.Dt;
		if (ActuallySprinting)
		{
			Stamina = Mathf.Max(0f, Stamina - Sv.StaminaDrainRate * dt);
			StaminaRegenTimer = Sv.StaminaRegenDelay;
		}
		else
		{
			StaminaRegenTimer = Mathf.Max(0f, StaminaRegenTimer - dt);
			if (StaminaRegenTimer <= 0f)
				Stamina = Mathf.Min(Sv.MaxStamina, Stamina + Sv.StaminaRegenRate * dt);
		}
	}

	/// <summary>Computes the desired horizontal speed for this tick based on sprint / shift / walk plus
	/// the crouch and ADS multipliers.</summary>
	private float ComputeTargetSpeed(MovementInput input)
	{
		float baseSpeed;
		if (ActuallySprinting)
		{
			baseSpeed = Sv.SprintSpeed;
		}
		else if (input.ShiftHeld) baseSpeed = Sv.ShiftSpeed;
		else baseSpeed = Sv.WalkSpeed;

		float speed = Mathf.Lerp(baseSpeed, Sv.CrouchSpeed, CrouchBlend);
		if (input.Weapon != null)
		{
			speed *= input.Weapon.MoveSpeedMul;
			if (AdsBlend > 0f)
			{
				float adsMul = Mathf.Lerp(1f, input.Weapon.AdsSpeedMul, AdsBlend);
				speed *= adsMul;
			}
		}
		return speed;
	}

	/// <summary>Applies horizontal velocity changes: counter-strafe, ground acceleration, friction, and
	/// air strafing.</summary>
	private void ApplyHorizontalMovement(ref Vector3 velocity, MovementInput input, float targetSpeed, float dt)
	{
		if (IsSliding) return;

		Vector3 inputDir = input.WishDir;
		bool hasInput = inputDir.LengthSquared() > 0.01f;
		if (hasInput) inputDir = inputDir.Normalized();

		Vector3 worldDir = input.BodyBasis * inputDir;
		Vector3 horizVel = new Vector3(velocity.X, 0f, velocity.Z);

		if (input.OnFloor)
		{
			Vector3 targetHoriz = hasInput ? worldDir * targetSpeed : Vector3.Zero;
			if (hasInput)
			{
				float opposingDot = horizVel.Dot(worldDir);
				if (opposingDot < 0f)
					horizVel -= worldDir * opposingDot;
				horizVel = horizVel.MoveToward(targetHoriz, Sv.GroundAcceleration * dt);
			}
			else
			{
				float decay = Mathf.Exp(-Sv.GroundFriction * dt);
				horizVel *= decay;
				if (horizVel.LengthSquared() < 0.01f) horizVel = Vector3.Zero;
			}
		}
		else if (hasInput)
		{
			float wishSpeed = Mathf.Min(targetSpeed, Sv.AirMaxWishSpeed);
			float currentInWishDir = horizVel.Dot(worldDir);
			float addSpeed = wishSpeed - currentInWishDir;
			if (addSpeed > 0f)
			{
				float accelSpeed = Sv.AirAcceleration * wishSpeed * dt;
				if (accelSpeed > addSpeed) accelSpeed = addSpeed;
				horizVel += worldDir * accelSpeed;
			}
		}

		velocity.X = horizVel.X;
		velocity.Z = horizVel.Z;
	}
}
