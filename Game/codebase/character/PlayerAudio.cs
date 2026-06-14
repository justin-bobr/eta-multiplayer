using Godot;

namespace Vantix.Character;

/// <summary>
/// Aggregates the FootstepAudio + WeaponAudio banks and exposes a wrapper API so callers don't touch the
/// audio nodes directly. Server scenes lack these nodes (null banks); all PlayX methods are null-safe.
/// </summary>
public sealed class PlayerAudio
{
	private readonly FootstepAudio _footsteps;
	private readonly WeaponAudio _weapon;

	/// <summary>Stores the two audio banks for later wrapper calls.</summary>
	public PlayerAudio(FootstepAudio footsteps, WeaponAudio weapon)
	{
		_footsteps = footsteps;
		_weapon = weapon;
	}

	/// <summary>Forwards the IsLocalPlayer flag plus an initial weapon preload to both banks.</summary>
	public void Configure(bool isLocalPlayer, WeaponStats activeWeapon)
	{
		if (_footsteps != null) _footsteps.IsLocalPlayer = isLocalPlayer;
		if (_weapon != null)
		{
			_weapon.IsLocalPlayer = isLocalPlayer;
			_weapon.Preload(activeWeapon);
		}
	}

	/// <summary>Plays a footstep sound at the given position.</summary>
	public void PlayStep(Vector3 pos, StringName material, float loud01, bool inTunnel, bool sprinting)
		=> _footsteps?.PlayStep(pos, material, loud01, inTunnel, sprinting);

	/// <summary>Plays a jump-takeoff footstep sound.</summary>
	public void PlayJump(Vector3 pos, StringName material, float loud01, bool inTunnel)
		=> _footsteps?.PlayJump(pos, material, loud01, inTunnel);

	/// <summary>Plays a landing footstep sound scaled by impact intensity.</summary>
	public void PlayLand(Vector3 pos, StringName material, float impact01, bool inTunnel)
		=> _footsteps?.PlayLand(pos, material, impact01, inTunnel);

	/// <summary>Plays the weapon shoot sound at the muzzle position with the given reverb environment.</summary>
	public void PlayShoot(WeaponStats weapon, Vector3 muzzlePos, ReverbEnv env)
		=> _weapon?.PlayShoot(weapon, muzzlePos, env);

	/// <summary>Plays the empty-magazine dry-fire click.</summary>
	public void PlayDryFire(WeaponStats weapon, Vector3 muzzlePos)
		=> _weapon?.PlayDryFire(weapon, muzzlePos);

	/// <summary>Plays the weapon reload sound.</summary>
	public void PlayReload(WeaponStats weapon, Vector3 muzzlePos)
		=> _weapon?.PlayReload(weapon, muzzlePos);
}
