using Godot;

namespace Vantix.Character;

/// <summary>
/// Server-authoritative character (real peer or bot). Runs the sim from replicated net input, resolves
/// hitscan with lag compensation, and poses the TPS skeleton for hitboxes. No FX/audio; a non-headless
/// server adds an eye-level spectate camera. Spawned from <c>server_player.tscn</c>.
/// </summary>
[Tool, GlobalClass]
public partial class ServerPlayer : NetworkPlayer
{
	private Godot.Collections.Array<Rid> _lagCompExcludes;
	private System.Collections.Generic.List<(Node3D hitbox, Transform3D worldXform, Shape3D shape)> _boneCastTargets;

	private MovementInput BuildMovementInputFromNet(float dt, in InputPacket p)
	{
		var rot = Rotation;
		rot.Y = p.ViewYaw;
		Rotation = rot;
		if (HeadPitch != null)
		{
			var hr = HeadPitch.Rotation;
			hr.X = p.ViewPitch;
			HeadPitch.Rotation = hr;
		}

		Vector3 wish = new(p.WishX, 0f, p.WishZ);
		if (wish.LengthSquared() > 1f) wish = wish.Normalized();

		return new MovementInput
		{
			TickIndex = CurrentTick,
			WishDir = wish,
			ViewYaw = p.ViewYaw,
			ViewPitch = p.ViewPitch,
			SprintHeld = p.SprintHeld,
			ShiftHeld = p.ShiftHeld,
			CrouchHeld = p.CrouchHeld,
			CrouchPressed = p.CrouchPressed,
			AdsHeld = p.AdsHeld,
			BreathHoldHeld = p.BreathHoldHeld,
			Weapon = ConVars.Weapons.AR15,
			JumpPressed = p.JumpPressed,
			OnFloor = IsOnFloor(),
			TouchingWall = IsOnWall(),
			WallNormal = IsOnWall() ? GetWallNormal() : Vector3.Zero,
			Dt = dt,
			Events = p.Events,
			InitialBits = (InputBits)p.InitialBits,
			InitialViewYaw = p.InitialViewYaw,
			InitialViewPitch = p.InitialViewPitch,
		};
	}

	/// <summary>Server-authoritative hitscan with lag compensation: other players' hitboxes are rewound to
	/// their historical positions before the cast. On a hit, applies damage, triggers death at HP 0, and
	/// broadcasts ShotFired.</summary>
	private void RunAuthoritativeHitscan(PhysicsDirectSpaceState3D space)
	{
		var server = NetMain.Instance?.Server;
		if (server == null) return;
		var myState = server.GetPeerStateForNetId(NetId);
		int rttMs = myState?.LastPingMs ?? 0;
		int halfRttTicks = Mathf.Clamp((rttMs * TickRate) / 2000, 0, 64);
		const int InterpDelayTicks = 6;
		long target = (long)CurrentTick - halfRttTicks - InterpDelayTicks;
		uint lagCompTick = (uint)Mathf.Max(0L, target);
		byte fireSubTick = myState != null && myState.HasLatestInput ? myState.LatestInput.FireSubTick : (byte)0;
		float fractionalLagCompTick = (float)lagCompTick + (fireSubTick / 256f);

		_boneCastTargets ??= new System.Collections.Generic.List<(Node3D, Transform3D, Shape3D)>();
		_boneCastTargets.Clear();
		bool useRewind = !ConVars.Sv.NoRewind;
		foreach (var other in server.AllPeers)
		{
			if (other == myState) continue;
			if (other.ServerAgent is not ServerPlayer otherPc) continue;
			if (otherPc._hitboxRig == null) continue;
			var shapes = otherPc._hitboxRig.CollisionShapes;
			var hitboxes = otherPc._hitboxRig.HitboxNodes;
			Transform3D[] rewound = useRewind
				? (fireSubTick > 0
					? otherPc.BoneHistory?.QueryFractional(fractionalLagCompTick)
					: otherPc.BoneHistory?.Query(lagCompTick))
				: null;
			int n = hitboxes.Count;
			if (useRewind && rewound != null) n = Mathf.Min(n, rewound.Length);
			for (int i = 0; i < n; i++)
			{
				var hb = hitboxes[i];
				var cs = shapes[i];
				if (hb == null || !GodotObject.IsInstanceValid(hb)) continue;
				if (cs?.Shape == null) continue;
				Transform3D worldXform = (useRewind && rewound != null) ? rewound[i] : cs.GlobalTransform;
				_boneCastTargets.Add((hb, worldXform, cs.Shape));
			}
		}

		if (_lagCompExcludes == null) _lagCompExcludes = new Godot.Collections.Array<Rid>();
		_lagCompExcludes.Clear();
		_lagCompExcludes.Add(GetRid());

		HitInfo worldHit = Hitscan.CastMulti(space, Movement.LastShotOrigin, Movement.LastShotDirection,
			HitscanRange, _lagCompExcludes, mask: 1u);
		bool worldHitBlocks = worldHit.Hit && !(worldHit.Collider?.IsInGroup("wallhit") ?? false);
		float maxDist = worldHitBlocks ? worldHit.Distance : HitscanRange;

		HitInfo boneHit = Hitscan.CastVsBoneShapes(Movement.LastShotOrigin, Movement.LastShotDirection,
			_boneCastTargets, maxDist);
		HitInfo lagHit = boneHit.Hit ? boneHit : worldHit;

		if (ConVars.Sv.DebugHitboxes && !boneHit.Hit)
		{
			System.Text.StringBuilder sb = new();
			sb.Append($"[sv-cast-miss] targets={_boneCastTargets.Count} worldHit={worldHit.Hit} maxDist={maxDist:F2} | ");
			foreach (var (hb, xform, shape) in _boneCastTargets)
			{
				Vector3 toCenter = xform.Origin - Movement.LastShotOrigin;
				float along = toCenter.Dot(Movement.LastShotDirection);
				Vector3 perpendicular = toCenter - Movement.LastShotDirection * along;
				float perpDist = perpendicular.Length();
				sb.Append($"{hb.Name}@dist{along:F1}/perp{perpDist:F2} ");
			}
			Dbg.Print(sb.ToString());
		}

		NetworkPlayer victim = lagHit.Hit ? HitboxRig.FindOwner(lagHit.Collider) : null;
		Dbg.Print($"[sv-fire] netId={NetId} tick={CurrentTick} origin={Movement.LastShotOrigin:F2} dir={Movement.LastShotDirection:F2} | hit={lagHit.Hit}{(lagHit.Hit ? $" collider={lagHit.Collider?.Name} ownerNetId={victim?.NetId.ToString() ?? "null"} dist={lagHit.Distance:F2}" : "")}");

		if (Dbg.Enabled)
		{
			foreach (var other in server.AllPeers)
			{
				if (other == myState || other.ServerAgent == null) continue;
				if (other.ServerAgent is not ServerPlayer otherPc) continue;
				if (otherPc._hitboxRig == null || otherPc._hitboxRig.HitboxNodes.Count == 0) continue;
				var headHb = otherPc._hitboxRig.HitboxNodes[0];
				Dbg.Print($"[sv-hitbox] netId={other.NetId} body={other.ServerAgent.GlobalPosition:F2} firstHitbox={headHb.Name} @ {headHb.GlobalPosition:F2}");
			}
		}
		if (victim != null && lagHit.Hit && IsHitObstructedByOpaqueWall(space, Movement.LastShotOrigin, lagHit.Position))
		{
			Dbg.Print($"[sv-fire] netId={NetId} wall-block: shot at netId={victim.NetId} blocked by opaque geometry between eye and target");
			lagHit.Hit = false;
			victim = null;
		}

		if (victim != null && victim.NetId != NetId && victim.NetId > 0)
		{
			var vs = server.GetPeerStateForNetId(victim.NetId);
			if (vs != null && vs.Hp > 0)
			{
				HitboxGroup group = HitboxRig.ReadGroup(lagHit.Collider);
				var weapon = ConVars.Weapons.AR15;
				int dmg = weapon.DamageFor(group);
				bool isHead = group == HitboxGroup.Head;

				int dmgToArmor = 0, dmgToHp = dmg;
				if (!isHead && vs.Armor > 0)
				{
					dmgToArmor = Mathf.Min(dmg / 2, vs.Armor);
					dmgToHp = dmg - dmgToArmor;
				}
				vs.Armor = (byte)Mathf.Max(0, vs.Armor - dmgToArmor);
				vs.Hp = (byte)Mathf.Max(0, vs.Hp - dmgToHp);
				vs.LastDamageTickMs = (long)Time.GetTicksMsec();

				string headTag = isHead ? " [HEAD]" : "";
				Dbg.Print($"[NetServer] HIT shooter={NetId} → victim={victim.NetId} weapon={weapon?.Name ?? "?"} part={group}{headTag} dmg={dmg} (armor-absorb={dmgToArmor}, hp-loss={dmgToHp}) → hp={vs.Hp} armor={vs.Armor}");
				server.SendHitTo(NetId, victim.NetId, group, (byte)Mathf.Min(255, dmg), vs.Hp, weaponId: 0);
				if (vs.Hp == 0)
				{
					Dbg.Print($"[NetServer] KILL shooter={NetId} killed victim={victim.NetId} via {group}{headTag} (weapon={weapon?.Name ?? "?"})");
					server.TriggerDeath(victim.NetId, NetId, weaponId: 0, isHeadshot: isHead);
				}
			}
		}

		server.BroadcastShotFired(
			NetId, weaponId: 0,
			Movement.LastShotOrigin, Movement.LastShotDirection,
			tracer: true,
			lagHit.Hit, lagHit.Position, lagHit.Normal,
			lagHit.Material.ToString());
	}

	/// <summary>True if an opaque wall (not in group "wallhit") sits between the shooter's eye and the impact.
	/// Iterates through penetrable walls (e.g. glass), capped at <c>MaxPenetrableChain</c>.</summary>
	private bool IsHitObstructedByOpaqueWall(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to)
	{
		if (space == null) return false;
		const int MaxPenetrableChain = 4;
		Vector3 dir = (to - from).Normalized();
		float remaining = from.DistanceTo(to) - 0.05f;
		if (remaining <= 0f) return false;
		Vector3 origin = from;
		for (int i = 0; i < MaxPenetrableChain; i++)
		{
			HitInfo wall = Hitscan.Cast(space, origin, dir, remaining, exclude: GetRid(), mask: 1u);
			if (!wall.Hit) return false;
			if (wall.Collider == null || !wall.Collider.IsInGroup("wallhit"))
				return true;
			float stepped = origin.DistanceTo(wall.Position) + 0.05f;
			if (stepped >= remaining) return false;
			origin = wall.Position + dir * 0.05f;
			remaining -= stepped;
		}
		return false;
	}

	/// <summary>Non-headless server only: adds an eye-level camera childed to the head and makes the body
	/// visible, so the operator sees what the player sees. The first server body claims the active camera.</summary>
	private void SetupServerSpectateCamera()
	{
		if (TpsVisual != null) TpsVisual.Visible = true;
		if (HeadPitch == null) return;

		var cam = new Camera3D { Name = "ServerSpectateCam" };
		HeadPitch.AddChild(cam);
		Camera3D current = GetViewport()?.GetCamera3D();
		if (current == null || current.Name != "ServerSpectateCam")
			cam.Current = true;
	}

	protected override void OnSimReady()
	{
		CollisionLayer = 1u << 4;
		CollisionMask = 1u | (1u << 4);
		if (TpsAnimTree != null)
			TpsAnimTree.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Physics;
		if (_hitboxRig != null && _hitboxRig.HitboxNodes.Count > 0)
		{
			BoneHistory = new BonePoseRewindBuffer();
			BoneHistory.Init(_hitboxRig.HitboxNodes.Count);
		}
		ViewMode = ViewMode.Disabled;
		ApplyViewMode();

		// Headless server renders nothing → free the body meshes. Non-headless keeps them and adds an
		// eye-level camera for the operator.
		if (DisplayServer.GetName() == "headless")
			DisableExpensiveSubtreeProcessing();
		else
			SetupServerSpectateCamera();
	}

	protected override void OnTickApplied()
	{
		if (NetInputSource.HasValue)
			LastAppliedInputTick = NetInputSource.Value.TickIndex;
	}

	protected override MovementInput BuildMovementInput(float dt)
		=> NetInputSource.HasValue ? BuildMovementInputFromNet(dt, NetInputSource.Value) : base.BuildMovementInput(dt);

	protected override void ResolveShot(PhysicsDirectSpaceState3D space) => RunAuthoritativeHitscan(space);

	protected override WeaponButtons SampleWeaponButtons()
	{
		if (!NetInputSource.HasValue) return default;
		var p = NetInputSource.Value;
		return new WeaponButtons { Fire = p.FirePressed, Reload = p.ReloadPressed, Inspect = p.InspectPressed, Ads = p.AdsHeld };
	}

	protected override void ResolveActiveSlot()
	{
		if (NetInputSource.HasValue) _activeSlot = NetInputSource.Value.SlotIsGrenade ? 1 : 0;
	}

	protected override void OnFootstepEvent(HitInfo ground, StringName material)
	{
		using (MiniProfiler.SampleServer("ServerPlayer.BroadcastFootstep"))
		{
			byte loudByte = (byte)Mathf.Clamp(Mathf.RoundToInt(FootstepLogic.StepLoudness * 255f), 0, 255);
			NetMain.Instance?.Server?.BroadcastFootstep(NetId, GlobalPosition, material.ToString(),
				loudByte, FootstepLogic.StepIsLeftFoot, Movement.ActuallySprinting);
		}
	}

	protected override void OnLandEvent(float impact) => NetMain.Instance?.Server?.BroadcastLand(NetId, impact);

	protected override void OnJumpEvent() => NetMain.Instance?.Server?.BroadcastJump(NetId);

	protected override void OnDropMagEvent() => NetMain.Instance?.Server?.BroadcastDropMag(NetId);

	protected override void DisableExpensiveSubtreeProcessing()
	{
		if (TpsSkeleton != null)
		{
			foreach (Node ch in TpsSkeleton.GetChildren())
				if (ch is MeshInstance3D) ch.QueueFree();
		}
		if (TpsVisual != null) TpsVisual.Visible = false;
	}
}
