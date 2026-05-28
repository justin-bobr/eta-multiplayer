using Godot;
using System.Collections.Generic;

/// <summary>
/// Deterministic projectile simulation for thrown grenades — shared between the real
/// <see cref="SmokeGrenade"/> and the throw aim guide (<see cref="GrenadeAimGuide"/>).
/// So the preview line never lies, both call into this central per-step code. Fixed
/// <see cref="FixedDt"/>, pure raycasts against the static map, no RigidBody and no randomness,
/// so identical spawn parameters produce identical landing positions on every client.
/// </summary>
public static class GrenadeTrajectory
{
	public const float BaseGravity = 17.5f;
	/// <summary>
	/// Effective gravity — set by PlayerCore from GrenadeRangeScale. This is the range knob,
	/// independent of throw speed (smaller = floatier = travels farther). All clients derive
	/// the same value from the same designer-set scale.
	/// </summary>
	public static float Gravity = BaseGravity;
	public const float Radius = 0.07f;
	public const float Restitution = 0.35f;
	public const float BounceFriction = 0.65f;
	public const float GroundDrag = 7f;
	public const float RestSpeed = 1.0f;
	public const float RestDuration = 0.18f;
	public const float MaxFlyTime = 5f;
	public const uint CollisionMask = 1;
	public const float FixedDt = 1f / 128f;

	/// <summary>
	/// Executes one deterministic projectile step: gravity, raycast move with bounce, ground drag.
	/// <paramref name="query"/> is reused (no per-step allocation). Returns whether the grenade has
	/// ground contact after the step (used for rest detection).
	/// </summary>
	public static bool Advance(PhysicsDirectSpaceState3D space, PhysicsRayQueryParameters3D query,
		ref Vector3 pos, ref Vector3 vel)
	{
		vel.Y -= Gravity * FixedDt;

		Vector3 move = vel * FixedDt;
		float moveLen = move.Length();
		if (moveLen > 1e-6f)
		{
			query.From = pos;
			query.To = pos + move / moveLen * (moveLen + Radius);
			var hit = space.IntersectRay(query);
			if (hit.Count > 0)
			{
				Vector3 n = (Vector3)hit["normal"];
				pos = (Vector3)hit["position"] + n * Radius;
				Vector3 vn = n * vel.Dot(n);
				Vector3 vt = vel - vn;
				vel = vt * BounceFriction - vn * Restitution;
			}
			else
			{
				pos += move;
			}
		}

		bool grounded = IsGrounded(space, query, pos, out Vector3 groundNormal);
		if (grounded)
		{
			Vector3 vn = groundNormal * vel.Dot(groundNormal);
			Vector3 vt = (vel - vn) * Mathf.Max(0f, 1f - GroundDrag * FixedDt);
			vel = vt + vn;
		}
		return grounded;
	}

	/// <summary>Down-raycast: returns true if the grenade is (just barely) standing on a surface.</summary>
	public static bool IsGrounded(PhysicsDirectSpaceState3D space, PhysicsRayQueryParameters3D query,
		Vector3 pos, out Vector3 normal)
	{
		normal = Vector3.Up;
		query.From = pos;
		query.To = pos + Vector3.Down * (Radius + 0.08f);
		var hit = space.IntersectRay(query);
		if (hit.Count == 0) return false;
		normal = (Vector3)hit["normal"];
		return true;
	}

	/// <summary>
	/// Full trajectory prediction from <paramref name="origin"/>/<paramref name="vel"/> until rest
	/// (or <see cref="MaxFlyTime"/>). Collects world-space path points in <paramref name="pathOut"/>
	/// (with a kink at every bounce) and returns the landing point.
	/// </summary>
	// Wiederverwendete Query + Exclude für Predict() — der AimGuide ruft Predict() jeden Frame
	// während der Spieler die Granate hält. Vorher: new PhysicsRayQueryParameters3D + new
	// Godot.Collections.Array<Rid> pro Aufruf = 2 Allocs × 60+ Hz.
	private static PhysicsRayQueryParameters3D _predictQuery;
	private static readonly Godot.Collections.Array<Rid> _predictExclude = new();

	public static void Predict(PhysicsDirectSpaceState3D space, Vector3 origin, Vector3 vel,
		Rid ownerExclude, List<Vector3> pathOut, out Vector3 landing, out Vector3 landingNormal)
	{
		pathOut.Clear();
		landingNormal = Vector3.Up;
		if (_predictQuery == null)
		{
			_predictQuery = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Right, CollisionMask);
			_predictQuery.Exclude = _predictExclude;
		}
		_predictExclude.Clear();
		_predictExclude.Add(ownerExclude);
		_predictQuery.CollisionMask = CollisionMask;
		var query = _predictQuery;

		Vector3 pos = origin;
		pathOut.Add(pos);
		float rest = 0f;
		int maxSteps = Mathf.CeilToInt(MaxFlyTime / FixedDt);

		for (int step = 0; step < maxSteps; step++)
		{
			bool grounded = Advance(space, query, ref pos, ref vel);
			pathOut.Add(pos);

			if (vel.Length() < RestSpeed && grounded) rest += FixedDt;
			else rest = 0f;
			if (rest >= RestDuration) break;
		}

		landing = pos;
		IsGrounded(space, query, pos, out landingNormal);
	}
}
