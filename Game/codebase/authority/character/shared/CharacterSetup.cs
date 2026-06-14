using Godot;

/// <summary>
/// Stateless body-setup helpers shared by the simulating drivers (LocalPlayer / ServerPlayer) for identical
/// capsule/crouch behaviour without a common base class. Each driver owns the resulting objects and passes
/// them back in per call. PuppetPlayer doesn't move and uses none of this.
/// </summary>
public static class CharacterSetup
{
	/// <summary>Duplicates the scene-shared capsule per instance (so crouch resize doesn't shrink every player),
	/// configures floor behaviour, and returns it for the caller to keep. Null if no usable capsule.</summary>
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
