using Godot;

/// <summary>
/// Stateless body-setup helpers shared by the simulating drivers (LocalPlayer / ServerPlayer) so
/// they get identical capsule / hitbox / crouch behaviour without a common base class. Each driver
/// owns the resulting objects (the duplicated capsule, the hitbox rig) and passes them back in per
/// call. PuppetPlayer does not move, so it uses none of this.
/// </summary>
public static class CharacterSetup
{
	/// <summary>Builds a unique capsule per instance (duplicated from the scene-shared .tres) so crouch
	/// resize does not shrink every player at once, configures floor behaviour, and returns the capsule
	/// for the caller to keep (crouch resize mutates it). Returns null when no usable capsule is present.</summary>
	public static CapsuleShape3D SetupCapsule(CharacterBody3D body, CollisionShape3D bodyCollision,
		float standHeight, float capsuleRadius, float floorMaxAngleDeg, float floorSnapDist)
	{
		if (bodyCollision == null) return null;
		if (bodyCollision.Shape is not CapsuleShape3D cap)
		{
			GD.PushWarning("[CharacterSetup] BodyCollision.Shape is not a CapsuleShape3D — crouch resize will not work");
			return null;
		}
		var capsule = (CapsuleShape3D)cap.Duplicate();
		capsule.Height = standHeight;
		capsule.Radius = capsuleRadius;
		bodyCollision.Shape = capsule;
		bodyCollision.Position = new Vector3(0f, standHeight / 2f, 0f);

		body.FloorMaxAngle = Mathf.DegToRad(floorMaxAngleDeg);
		body.FloorSnapLength = floorSnapDist;
		body.FloorBlockOnWall = true;
		body.FloorStopOnSlope = false;
		// MaxSlides 2 (default 4) halves per-MoveAndSlide cost on wall-slides; 2 iterations cover
		// flat-wall + step-up. Raise to 3 if players get stuck on sharp wedge corners.
		body.MaxSlides = 2;
		return capsule;
	}

	/// <summary>Spawns the per-bone hitbox rig under <paramref name="owner"/> if a skeleton is present
	/// (server: hitscan damage resolve; client: visualization). Returns the rig or null.</summary>
	public static HitboxRig SetupHitboxRig(Node owner, Skeleton3D skeleton)
	{
		if (skeleton == null) return null;
		var rig = new HitboxRig { Skeleton = skeleton, Name = "HitboxRig" };
		owner.AddChild(rig);
		rig.Build();
		return rig;
	}

	/// <summary>Default client body layers: layer 2 (Characters), masks world + other characters.</summary>
	public static void ConfigureClientLayers(CharacterBody3D body)
	{
		body.CollisionLayer = 1u << 1;
		body.CollisionMask = 1u | (1u << 1);
	}

	/// <summary>Server-agent body layers: layer 5, masks world + other server agents (client capsules
	/// on layer 2 are not masked, so there is no cross-push).</summary>
	public static void ConfigureServerLayers(CharacterBody3D body)
	{
		body.CollisionLayer = 1u << 4;
		body.CollisionMask = 1u | (1u << 4);
	}

	/// <summary>Live capsule resize from the crouch blend. Skips sub-0.1mm deltas (resize re-cooks the
	/// shape and is not visible). Eye-height adjustment is the driver's job (it owns the head pivot).</summary>
	public static void ApplyCrouchHeight(CapsuleShape3D capsule, CollisionShape3D bodyCollision,
		float standHeight, float crouchHeight, float crouchBlend)
	{
		if (capsule == null || bodyCollision == null) return;
		float h = Mathf.Lerp(standHeight, crouchHeight, crouchBlend);
		if (Mathf.Abs(capsule.Height - h) < 0.0001f) return;
		capsule.Height = h;
		var pos = bodyCollision.Position;
		pos.Y = h * 0.5f;
		bodyCollision.Position = pos;
	}
}
