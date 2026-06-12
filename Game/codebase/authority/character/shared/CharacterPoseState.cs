using Godot;

/// <summary>Which driver owns a given <see cref="NetworkPlayer"/> view: the local player, a
/// remote puppet, or a headless server agent.</summary>
public enum PresentationMode { Local, Remote, Server }

/// <summary>
/// Per-frame snapshot of the simulation state the view needs to pose the body and drive blends.
/// The driver fills this each frame (Local/Server from its own sim, Puppet from interpolated
/// snapshots) and pushes it to <see cref="NetworkPlayer.PoseBody"/>. A plain struct passed by
/// <c>in</c> reference — no per-frame allocation, and the view cannot mutate sim state through it.
/// </summary>
public struct CharacterPoseState
{
	// Body motion (TPS locomotion blend + air state).
	public Vector3 Velocity;
	public float HorizontalSpeed;
	public bool OnFloor;
	public bool Airborne;
	public bool DidJump;

	// Aim / view orientation.
	public float HeadPitchRad;
	public float BodyYawRad;
	public float SpineTwist;

	// Blends (already smoothed by the sim where relevant).
	public float AdsBlend;
	public float CrouchBlend;
	public float WeaponRaiseBlend;

	// Locomotion mode hints.
	public bool ActuallySprinting;
	public bool IsSliding;
	public bool IsWallClinging;

	// Weapon / action state for montages + HUD-adjacent view logic.
	public int ShotIndex;
	public int CurrentMag;
	public int FireMode;
	public int ActiveSlot;
	public bool IsReloading;
	public bool IsInspecting;
	public Vector3 AimPunch;
}
