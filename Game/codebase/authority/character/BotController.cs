using Godot;
using System.Collections.Generic;

/// <summary>Combat-side context the BotController needs per tick. Bundled into a struct so the
/// Tick signature stays readable instead of carrying 10+ scalar parameters. Built once per bot
/// by <see cref="NetServer.UpdateBotInputs"/>.</summary>
public struct BotCombatContext
{
	public List<PeerState> AllPeers;
	public byte OwnNetId;
	public Team OwnTeam;
	public Rid SelfBodyRid;
	public int Difficulty;
	public int TickRate;
	/// <summary>True when the bot's own magazine is empty and it's not already reloading. Drives
	/// the per-tick ReloadPressed flag — controller sets it true, MovementController's edge detector
	/// fires the reload exactly once, then keeps IsReloading=true through the reload animation so
	/// subsequent true-frames are no-ops.</summary>
	public bool NeedsReload;
}

/// <summary>
/// Drives a bot's <see cref="ServerBaseCharacter.NetInputSource"/> so it walks between waypoints,
/// steering around walls and other obstacles via short-range raycast probes. No NavMesh —
/// pathfinding is replaced by reactive avoidance: each tick the controller picks a target, then
/// probes the desired heading and several offset headings, and chooses the first one that's
/// clear within <see cref="ProbeDistance"/>. Stuck (no progress for <see cref="StuckCheckTicks"/>
/// without movement) triggers an immediate target re-pick so a bot pinned against a corner doesn't
/// stay there.
///
/// Bots WALK only — sprint stays off. Matches CS-style competitive bot pacing: predictable
/// footstep cadence, lets enemies hear them, and stops them from tunneling into geometry at
/// high speed when avoidance is borderline.
///
/// Pure logic class — held as a plain field on <see cref="ServerBotPlayer"/>, ticked by
/// <see cref="NetServer.Poll"/> before <c>FeedInputsToAgents</c> so the next physics step reads
/// the fresh InputPacket. Per-instance raycast query + result are pooled so the controller adds
/// zero allocations per tick.
/// </summary>
public class BotController
{
	/// <summary>Distance below which the current target counts as "reached".</summary>
	private const float ArriveRadius = 2.5f;
	/// <summary>Hard timeout per target. ~5 seconds at 128 Hz — covers a normal traversal across
	/// dust2 mid; if the bot still hasn't arrived, the path is probably unreachable.</summary>
	private const uint MaxTicksPerTarget = 640;
	/// <summary>Stuck check: every second we compare current position to the last sample. If the
	/// bot moved less than <see cref="StuckMinMovedMeters"/> in that window it's pinned somewhere
	/// (corner, against a doorframe, etc.) and we re-pick the target immediately.</summary>
	private const uint StuckCheckTicks = 128;
	private const float StuckMinMovedMeters = 0.5f;
	/// <summary>Max body-yaw turn per tick (radians). 0.05 rad ≈ 6.4 rad/sec ≈ 366°/sec — quick
	/// enough that avoidance reacts within a few frames, slow enough that the bot doesn't snap
	/// instantly. CS-style "panning" feel.</summary>
	private const float MaxYawTurnPerTick = 0.05f;
	/// <summary>Forward probe distance: how far ahead the avoidance ray looks for walls/agents.
	/// Walking speed is ~3-4 m/s, so 3.5m is ~1 second of reaction window — long enough to fully
	/// reorient at the current yaw-turn rate before the body actually touches geometry.</summary>
	private const float ProbeDistance = 3.5f;
	/// <summary>Vertical offset for the body's "torso" probe height. Used for wall detection at
	/// roughly chest level — where most map walls / doorframes register.</summary>
	private const float ProbeHeight = 1.0f;
	/// <summary>Lower probe height: catches knee-level obstacles (crates, low cover, fences) that
	/// the torso probe would miss but the body's capsule WILL hit.</summary>
	private const float LowProbeHeight = 0.4f;
	/// <summary>Half-width of the body capsule footprint. Three parallel probes (left edge, centre,
	/// right edge) are cast so we don't think a narrow gap is clear when the body wouldn't fit. The
	/// player capsule radius is ~0.4, this matches.</summary>
	private const float ProbeHalfWidth = 0.4f;
	/// <summary>Yaw offsets (radians) tried in order when the desired heading is blocked. Symmetric
	/// pairs let the bot pick whichever side is clear; the increasing magnitude means it widens
	/// the search smoothly. ~80° max sweep before giving up and going with the original heading
	/// (= bot walks into the wall, stuck-detection picks a new target shortly after).</summary>
	private static readonly float[] ProbeOffsets =
	{
		0.0f,
		-0.35f, 0.35f,
		-0.7f,  0.7f,
		-1.05f, 1.05f,
		-1.4f,  1.4f,
		-1.75f, 1.75f,    // ±100° — bot starts looking sideways
		-2.1f,  2.1f,     // ±120° — diagonal-back
		-2.6f,  2.6f,     // ±150° — almost full backtrack, last resort to escape dead-end corners
	};

	private Vector3 _target;
	private float _smoothYaw;
	private float _smoothPitch;
	private uint _targetSetTick;
	private Vector3 _stuckCheckPos;
	private uint _stuckCheckTick;
	private bool _initialized;

	// Combat state. _targetEnemyNetId is the locked target (0 if none); _targetAcquiredTick is the
	// first tick we saw it (drives reaction-delay). _aimAtHead alternates per acquire on diff 2 so
	// the bot mixes body+head shots instead of always picking the same point.
	private byte _targetEnemyNetId;
	private uint _targetAcquiredTick;
	private bool _aimAtHead;

	// Pooled raycast resources — single-threaded server tick, single bot accesses them per call.
	private PhysicsRayQueryParameters3D _query;
	private readonly PhysicsRayQueryResult3D _result = new();

	private readonly RandomNumberGenerator _rng = new();

	/// <summary>Eye height of the standing body — must match BaseCharacter.StandEyeHeight default
	/// so the LOS raycast originates at the same point the snapshot/hitscan systems treat as eye.</summary>
	private const float EyeHeight = 1.7f;
	/// <summary>Maximum engagement distance. Beyond this bots ignore targets — keeps them from
	/// sniping cross-map and matches CS-style mid-range engagement zones.</summary>
	private const float MaxEngagementDistance = 50f;

	/// <summary>Called once after <see cref="NetServer.SpawnBot"/> ties this controller to a body.
	/// Seeds the first target so the next Tick has something to walk toward. Idempotent —
	/// re-calling resets the target (useful on respawn).</summary>
	public void Init(Vector3 startPos, float startYaw, IReadOnlyList<Vector3> waypoints, uint currentTick)
	{
		_rng.Randomize();
		_smoothYaw = startYaw;
		_stuckCheckPos = startPos;
		_stuckCheckTick = currentTick;
		// Init runs before the bot has been ticked once, so we don't have a space reference yet —
		// pick a target without line-of-sight checking. The next Tick will re-pick via LOS as soon
		// as space is available, so any unreachable initial pick gets corrected within ~1 second
		// via the stuck-detect window.
		PickNewTarget(startPos, waypoints, currentTick, space: null, probeMask: 0);
		_initialized = true;
	}

	/// <summary>Produces an <see cref="InputPacket"/> for this server tick. Two-phase logic:
	///   1. Detect a visible enemy in range. If found: smooth-turn body+pitch toward target,
	///      fire when aim is aligned AND reaction-delay elapsed. Stop walking while engaging.
	///   2. No enemy: regular wander — pick LOS waypoint, probe-avoid walls, walk forward.
	/// The wander code path is identical to before; combat is layered on top so a bot that loses
	/// its target falls straight back into wandering without missing a beat.</summary>
	public InputPacket Tick(uint tickIndex, Vector3 currentPos, IReadOnlyList<Vector3> waypoints,
		PhysicsDirectSpaceState3D space, uint probeMask, BotCombatContext combat)
	{
		if (!_initialized) Init(currentPos, _smoothYaw, waypoints, tickIndex);

		// === Combat phase: try to lock onto a visible enemy ===
		PeerState enemy = DetectBestEnemy(currentPos, space, combat);
		if (enemy != null)
		{
			if (_targetEnemyNetId != enemy.NetId)
			{
				_targetEnemyNetId = enemy.NetId;
				_targetAcquiredTick = tickIndex;
				_aimAtHead = Mathf.Clamp(combat.Difficulty, 0, 3) switch
				{
					>= 3 => true,                       // diff 3 = always head
					2 => _rng.Randf() > 0.5f,           // diff 2 = 50/50 body/head per acquire
					_ => false,                         // diff 0/1 = body-or-lower
				};
				_stuckCheckPos = currentPos; // reset stuck-detect: we WANT to stop walking now
				_stuckCheckTick = tickIndex;
			}

			Vector3 enemyAimPoint = ComputeAimPoint(enemy.ServerAgent.GlobalPosition, combat.Difficulty);
			Vector3 eye = currentPos + Vector3.Up * EyeHeight;
			Vector3 toEnemy = enemyAimPoint - eye;
			float horiz = Mathf.Sqrt(toEnemy.X * toEnemy.X + toEnemy.Z * toEnemy.Z);
			float desiredYaw = Mathf.Atan2(-toEnemy.X, -toEnemy.Z);
			float desiredPitch = horiz > 0.01f ? Mathf.Atan2(toEnemy.Y, horiz) : 0f;

			float combatYawRate = GetCombatYawTurnPerTick(combat.Difficulty, combat.TickRate);
			// Deadband: sub-0.2° aim corrections are skipped. Without this the body capsule's
			// per-tick gravity / floor-snap micro-drift moves the bot's eye 1-2mm per frame,
			// which shifts desiredYaw by a fraction of a degree, and smooth_yaw chases every
			// frame — visible as a constant high-frequency tremor in the screenshot.
			const float AimDeadband = 0.003f;
			float rawYawDelta = Mathf.AngleDifference(_smoothYaw, desiredYaw);
			if (Mathf.Abs(rawYawDelta) > AimDeadband)
				_smoothYaw += Mathf.Clamp(rawYawDelta, -combatYawRate, combatYawRate);
			float rawPitchDelta = desiredPitch - _smoothPitch;
			if (Mathf.Abs(rawPitchDelta) > AimDeadband)
				_smoothPitch = Mathf.Clamp(_smoothPitch + Mathf.Clamp(rawPitchDelta, -combatYawRate, combatYawRate), -1.4f, 1.4f);

			// Fire when aim is close enough AND the reaction-delay has elapsed since first lock.
			// AimAligned threshold loosens with lower skill so diff-0 bots happily spray slightly
			// off-target while diff-3 bots wait for a tight lock.
			float alignThreshold = combat.Difficulty switch { >= 3 => 0.05f, 2 => 0.10f, 1 => 0.18f, _ => 0.3f };
			bool aimAligned = Mathf.Abs(Mathf.AngleDifference(_smoothYaw, desiredYaw)) < alignThreshold;
			uint reactionTicks = GetReactionTicks(combat.Difficulty, combat.TickRate);
			bool reactionElapsed = tickIndex - _targetAcquiredTick >= reactionTicks;
			bool canFire = aimAligned && reactionElapsed;

			// Stop while engaging — stable aim, no run-and-gun. CS-bot behaviour.
			// While reloading: still aim, don't fire (CanFire stays false during IsReloading anyway,
			// but FirePressed=false skips the wantsFire branch entirely). Reload edge fires once.
			return new InputPacket
			{
				TickIndex = tickIndex,
				ViewYaw = _smoothYaw,
				ViewPitch = _smoothPitch,
				InitialViewYaw = _smoothYaw,
				InitialViewPitch = _smoothPitch,
				WishX = 0f,
				WishZ = 0f,
				SprintHeld = false,
				FirePressed = canFire && !combat.NeedsReload,
				ReloadPressed = combat.NeedsReload,
				Events = null,
				InitialBits = 0,
			};
		}

		// === No enemy: clear combat lock + run regular wander logic ===
		_targetEnemyNetId = 0;
		_smoothPitch = Mathf.MoveToward(_smoothPitch, 0f, 0.05f); // relax pitch back to level

		if (waypoints == null || waypoints.Count == 0)
		{
			return new InputPacket { TickIndex = tickIndex, ViewYaw = _smoothYaw, InitialViewYaw = _smoothYaw };
		}

		// Stuck-detection: window-based progress check. Stops the bot wasting ~5 seconds
		// pinned against geometry waiting for the per-target timeout to elapse.
		if (tickIndex - _stuckCheckTick >= StuckCheckTicks)
		{
			if ((currentPos - _stuckCheckPos).LengthSquared() < StuckMinMovedMeters * StuckMinMovedMeters)
				PickNewTarget(currentPos, waypoints, tickIndex, space, probeMask);
			_stuckCheckPos = currentPos;
			_stuckCheckTick = tickIndex;
		}

		Vector3 delta = _target - currentPos;
		delta.Y = 0f;
		float distance = delta.Length();

		if (distance < ArriveRadius || tickIndex - _targetSetTick > MaxTicksPerTarget)
		{
			PickNewTarget(currentPos, waypoints, tickIndex, space, probeMask);
			delta = _target - currentPos;
			delta.Y = 0f;
			distance = delta.Length();
		}

		// Body's local forward is -Z (Godot convention). yaw=0 -> facing world -Z; yaw rotates around Y.
		// atan2(-dx, -dz) maps a world-space target delta to the yaw that points local -Z at it.
		float wanderYaw = distance > 0.01f ? Mathf.Atan2(-delta.X, -delta.Z) : _smoothYaw;
		if (space != null)
			wanderYaw = FindClearYaw(space, currentPos, wanderYaw, probeMask);

		float wanderYawDelta = Mathf.AngleDifference(_smoothYaw, wanderYaw);
		wanderYawDelta = Mathf.Clamp(wanderYawDelta, -MaxYawTurnPerTick, MaxYawTurnPerTick);
		_smoothYaw += wanderYawDelta;

		return new InputPacket
		{
			TickIndex = tickIndex,
			ViewYaw = _smoothYaw,
			ViewPitch = _smoothPitch,
			InitialViewYaw = _smoothYaw,
			InitialViewPitch = _smoothPitch,
			WishX = 0f,
			WishZ = -1f,
			SprintHeld = false,
			// Tactical reload while wandering — top up the mag any time it's empty out of combat.
			// Edge-triggered in MovementController so keeping ReloadPressed=true during a multi-
			// second reload animation is harmless (subsequent ticks see IsReloading=true → no-op).
			ReloadPressed = combat.NeedsReload,
			Events = null,
			InitialBits = 0,
		};
	}

	/// <summary>Reaction delay before first shot, in ticks. Converted from the ms target via the
	/// supplied tick rate so the same constants behave identically at 64 vs 128 Hz.</summary>
	private static uint GetReactionTicks(int difficulty, int tickRate)
	{
		int ms = Mathf.Clamp(difficulty, 0, 3) switch { >= 3 => 80, 2 => 200, 1 => 350, _ => 500 };
		return (uint)Mathf.Max(1, (ms * tickRate) / 1000);
	}

	/// <summary>Max yaw turn per tick during combat. Higher difficulty = faster snap-to-aim.
	/// Equivalent rates: diff0 ≈ 170°/s, diff1 ≈ 290°/s, diff2 ≈ 460°/s, diff3 ≈ 800°/s.</summary>
	private static float GetCombatYawTurnPerTick(int difficulty, int tickRate)
	{
		float radPerSec = Mathf.Clamp(difficulty, 0, 3) switch { >= 3 => 14f, 2 => 8f, 1 => 5f, _ => 3f };
		return radPerSec / Mathf.Max(1, tickRate);
	}

	/// <summary>Aim point offset from enemy feet position. Lower difficulty = aim lower (often misses
	/// into the floor); higher difficulty = aim at head. Diff 2 alternates body/head per acquire
	/// via the <see cref="_aimAtHead"/> flag.</summary>
	private Vector3 ComputeAimPoint(Vector3 enemyFeetPos, int difficulty)
	{
		float aimY = Mathf.Clamp(difficulty, 0, 3) switch
		{
			>= 3 => 1.65f,                  // head
			2 => _aimAtHead ? 1.65f : 1.0f, // 50/50 body or head (decided on acquire)
			1 => 1.0f,                      // chest
			_ => 0.4f,                      // legs — diff-0 sprays into the floor
		};
		return enemyFeetPos + Vector3.Up * aimY;
	}

	/// <summary>Iterates the peer list for hostile alive bodies within engagement range, casts a
	/// single LOS ray to each candidate and returns the closest visible one. Skips the bot itself
	/// (NetId match), same-team peers, dead peers (Hp==0), and peers without a spawned agent.
	/// Returns <c>null</c> if nothing is visible — caller then runs the wander path.</summary>
	private PeerState DetectBestEnemy(Vector3 currentPos, PhysicsDirectSpaceState3D space, BotCombatContext combat)
	{
		if (space == null || combat.AllPeers == null) return null;
		Vector3 eye = currentPos + Vector3.Up * EyeHeight;
		PeerState best = null;
		float bestDistSq = MaxEngagementDistance * MaxEngagementDistance;
		foreach (var peer in combat.AllPeers)
		{
			if (peer.NetId == combat.OwnNetId) continue;
			// Don't target other bots — they should stay wandering for the player's benefit instead
			// of pairing off and gunning each other down. Real CS bots do shoot bots, but the user
			// wants the dust2 backdrop full of walking bots + occasional engagement when the player
			// crosses one. Drop this filter later when an actual deathmatch mode is wired up.
			if (peer.IsBot) continue;
			if (peer.Team == combat.OwnTeam) continue;
			if (peer.Hp == 0) continue;
			var agent = peer.ServerAgent;
			if (agent == null || agent.IsDead || agent.IsFrozen) continue;

			Vector3 enemyFeet = agent.GlobalPosition;
			float distSq = (enemyFeet - currentPos).LengthSquared();
			if (distSq > bestDistSq) continue;

			// Triple-ray LOS: head + chest + waist of the enemy. A single chest-to-chest probe used
			// to find tiny gaps between adjacent map meshes that aren't really walkable / shootable
			// — bots "saw through walls" through these slits. Requiring ALL THREE rays clear means
			// the visible silhouette must be unobstructed, which matches what the player perceives
			// as "the bot can see me".
			if (!HasFullLineOfSight(space, eye, enemyFeet))
				continue;

			best = peer;
			bestDistSq = distSq;
		}
		return best;
	}

	/// <summary>True iff three rays from the observer's eye to the enemy's head, chest and waist
	/// are all clear of world geometry (mask=1, bit 0 = layer 1, the default StaticBody3D layer
	/// used by dust2's collision meshes). Mask deliberately excludes server-agent bodies (bit 4)
	/// because the enemy IS a server-agent body and would otherwise block its own visibility.</summary>
	private bool HasFullLineOfSight(PhysicsDirectSpaceState3D space, Vector3 eye, Vector3 enemyFeetPos)
	{
		if (_query == null)
		{
			_query = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Right);
			_query.CollideWithAreas = false;
			_query.CollideWithBodies = true;
		}
		_query.CollisionMask = 1u;
		_query.From = eye;

		_query.To = enemyFeetPos + Vector3.Up * 1.65f; // head
		if (space.IntersectRayInto(_query, _result)) return false;
		_query.To = enemyFeetPos + Vector3.Up * 1.0f;  // chest
		if (space.IntersectRayInto(_query, _result)) return false;
		_query.To = enemyFeetPos + Vector3.Up * 0.5f;  // waist
		if (space.IntersectRayInto(_query, _result)) return false;
		return true;
	}

	/// <summary>Probes the desired heading and progressively-wider offsets, returning the first
	/// yaw where the bot's full capsule footprint (left edge / centre / right edge at two heights:
	/// chest + knee) has a clear forward lane. Single-strip probes missed narrow gaps and crate-
	/// height obstacles that the body would still hit. Returns the original desiredYaw if every
	/// offset is blocked — the bot then walks into a wall briefly and stuck-detection re-picks
	/// the target within ~1 second.</summary>
	private float FindClearYaw(PhysicsDirectSpaceState3D space, Vector3 currentPos, float desiredYaw, uint probeMask)
	{
		foreach (float offset in ProbeOffsets)
		{
			float candidateYaw = desiredYaw + offset;
			if (IsLaneClear(space, currentPos, candidateYaw, probeMask))
				return candidateYaw;
		}
		return desiredYaw;
	}

	/// <summary>True iff six probes (left edge + centre + right edge × chest + knee) along
	/// <paramref name="yaw"/> all return clear. Forward direction follows the same convention
	/// as <see cref="MovementInput.AimDirection"/>: (-sin yaw, 0, -cos yaw). Right is the 90°
	/// clockwise rotation of forward in the XZ plane = (-cos yaw, 0, +sin yaw).</summary>
	private bool IsLaneClear(PhysicsDirectSpaceState3D space, Vector3 currentPos, float yaw, uint probeMask)
	{
		if (_query == null)
		{
			_query = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Right);
			_query.CollideWithAreas = false;
			_query.CollideWithBodies = true;
		}
		_query.CollisionMask = probeMask;

		Vector3 forward = new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
		Vector3 right = new Vector3(-Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
		Vector3 chestBase = currentPos + Vector3.Up * ProbeHeight;
		Vector3 kneeBase = currentPos + Vector3.Up * LowProbeHeight;
		Vector3 sideOffset = right * ProbeHalfWidth;
		Vector3 forwardOffset = forward * ProbeDistance;

		return ProbeRay(space, chestBase - sideOffset, forwardOffset)
		    && ProbeRay(space, chestBase, forwardOffset)
		    && ProbeRay(space, chestBase + sideOffset, forwardOffset)
		    && ProbeRay(space, kneeBase - sideOffset, forwardOffset)
		    && ProbeRay(space, kneeBase, forwardOffset)
		    && ProbeRay(space, kneeBase + sideOffset, forwardOffset);
	}

	/// <summary>Single-ray helper used by <see cref="IsLaneClear"/>. Returns true if the ray
	/// segment is clear (no hit) — matches the boolean sense the caller wants.</summary>
	private bool ProbeRay(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 delta)
	{
		_query.From = from;
		_query.To = from + delta;
		return !space.IntersectRayInto(_query, _result);
	}

	/// <summary>Picks a random waypoint that satisfies two constraints:
	///   1. Far enough away (&gt; <see cref="MinTargetDistanceSquared"/>) that the bot actually moves
	///   2. Reachable by line-of-sight — a single raycast from the bot's chest height to the
	///      candidate's chest height must hit nothing in <paramref name="probeMask"/>
	/// This is the CS-style trick that lets a bot navigate maze-like maps without a NavMesh:
	/// only consider targets you can SEE, then the reactive probe inside <see cref="Tick"/>
	/// handles the close-range steering. A target behind a wall is never picked, so the bot
	/// can't try to walk through that wall.
	/// Falls through to a distance-only pick if no LOS-clear candidate is found in
	/// <see cref="MaxPickAttempts"/> tries — happens when the bot is in a deep dead-end with
	/// no visible waypoint. Stuck-detection then re-picks shortly after, hopefully from a
	/// position with better visibility.</summary>
	private const float MinTargetDistanceSquared = 25f; // 5m
	private const int MaxPickAttempts = 12;
	private void PickNewTarget(Vector3 currentPos, IReadOnlyList<Vector3> waypoints, uint tickIndex,
		PhysicsDirectSpaceState3D space, uint probeMask)
	{
		_targetSetTick = tickIndex;
		if (waypoints == null || waypoints.Count == 0)
		{
			_target = currentPos;
			return;
		}

		Vector3 chest = currentPos + Vector3.Up * ProbeHeight;
		Vector3 fallback = Vector3.Zero;
		bool haveFallback = false;
		for (int attempt = 0; attempt < MaxPickAttempts; attempt++)
		{
			int idx = _rng.RandiRange(0, waypoints.Count - 1);
			Vector3 candidate = waypoints[idx];
			if ((candidate - currentPos).LengthSquared() < MinTargetDistanceSquared)
				continue;
			if (!haveFallback) { fallback = candidate; haveFallback = true; }
			if (space != null && HasLineOfSight(space, chest, candidate, probeMask))
			{
				_target = candidate;
				return;
			}
			else if (space == null)
			{
				_target = candidate;
				return;
			}
		}
		_target = haveFallback ? fallback : waypoints[_rng.RandiRange(0, waypoints.Count - 1)];
	}

	/// <summary>Triple-height line-of-sight check from the bot's chest to a candidate world point.
	/// Head + chest + waist rays all need to be clear of <paramref name="probeMask"/> geometry.
	/// Same rationale as the combat-side <see cref="HasFullLineOfSight"/>: a single chest-to-chest
	/// probe slipped through narrow geometry slits in dust2 (texture-decorative wall holes between
	/// adjacent meshes that aren't really walkable). Bot then picked a target through the slit and
	/// stood facing the wall. Three rays kill that.</summary>
	private bool HasLineOfSight(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to, uint probeMask)
	{
		if (_query == null)
		{
			_query = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Right);
			_query.CollideWithAreas = false;
			_query.CollideWithBodies = true;
		}
		_query.CollisionMask = probeMask;
		_query.From = from;
		_query.To = to + Vector3.Up * 1.65f;
		if (space.IntersectRayInto(_query, _result)) return false;
		_query.To = to + Vector3.Up * ProbeHeight;
		if (space.IntersectRayInto(_query, _result)) return false;
		_query.To = to + Vector3.Up * 0.5f;
		if (space.IntersectRayInto(_query, _result)) return false;
		return true;
	}
}
