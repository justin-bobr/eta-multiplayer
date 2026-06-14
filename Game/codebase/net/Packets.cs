using Godot;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Vantix.Net;

/// <summary>Central packet read/write helpers. Each packet starts with a <see cref="PacketType"/> byte,
/// then a type-specific body.
/// Channels: Input + Snapshot are Unreliable (channel 0, drops discarded); all gameplay events and the
/// handshake are ReliableOrdered (channel 1). Token is a variable-length byte array (GUID or auth token).</summary>
public static class Packets
{
	/// <summary>Current protocol version. Bump on any incompatible wire change.
	/// v2: snapshot Pos/Vel cm-quantised int16; material a byte id. v3: delta-baseline snapshot compression
	/// (baselineTick + per-player field mask; input carries ackedSnapshotTick). v4: input redundancy (N bodies,
	/// dedupe by tickIndex). v5: subtick fire-timing (FireSubTick byte). v6: subtick movement (InitialBits +
	/// initial yaw/pitch + EventCount + N SubtickEvents).</summary>
	public const ushort ProtocolVersion = 6;

	/// <summary>Hard wire cap on subtick events per input body. Must match <c>NetworkPlayer.MaxSubtickEventsPerTick</c>;
	/// server rejects higher counts (cheat + bandwidth guard).</summary>
	public const int MaxSubtickEventsWire = 16;

	/// <summary>Inputs bundled redundantly per packet. 3 covers 2 consecutive drops.</summary>
	public const int MaxInputRedundancy = 3;

	/// <summary>"No baseline" sentinel (full snapshot / nothing received). The server starts at tick 1 so tick 0 never collides.</summary>
	public const uint NoBaselineTick = 0u;

	private static readonly string[] MaterialNames = new[]
	{
		"default",
		"flesh",
		"concrete", "concrete_2", "metal", "metal_2", "wood", "wood_2", "glass",
		"gravel", "gravel_2", "dirt", "dirt_2", "sand", "wet_sand", "mud",
		"grass", "grass_2", "high_grass", "ice", "snow",
		"carpet_hard", "carpet_wood", "deep_water", "shallow_water_wet_surface",
		"undergrowth_leaves", "broken_glass_glass_shards",
		"glass_shards_concrete", "glass_shards_concrete_2", "glass_shards_metal",
		"glass_shards_metal_2", "glass_shards_wood", "glass_shards_wood_2",
	};
	private static readonly System.Collections.Generic.Dictionary<string, byte> _materialIdMap = BuildMaterialIdMap();
	/// <summary>Builds the string-to-id lookup for the material table at type initialisation time.</summary>
	private static System.Collections.Generic.Dictionary<string, byte> BuildMaterialIdMap()
	{
		var m = new System.Collections.Generic.Dictionary<string, byte>(MaterialNames.Length);
		for (int i = 0; i < MaterialNames.Length; i++) m[MaterialNames[i]] = (byte)i;
		return m;
	}
	/// <summary>Returns the wire-format byte id for a material name, falling back to 0 ("default").</summary>
	public static byte MaterialToId(string m) =>
		!string.IsNullOrEmpty(m) && _materialIdMap.TryGetValue(m, out var id) ? id : (byte)0;
	/// <summary>Returns the material name for a wire-format byte id, falling back to "default" when out of range.</summary>
	public static string IdToMaterial(byte id) =>
		id < MaterialNames.Length ? MaterialNames[id] : "default";

	/// <summary>Allocates a new <see cref="NetDataWriter"/> pre-stamped with the given packet type byte.</summary>
	public static NetDataWriter Begin(PacketType type)
	{
		var w = new NetDataWriter();
		w.Put((byte)type);
		return w;
	}

	/// <summary>Writes a Vector3 as three IEEE-754 floats (12 bytes).</summary>
	public static void PutVec3(this NetDataWriter w, Vector3 v)
	{
		w.Put(v.X);
		w.Put(v.Y);
		w.Put(v.Z);
	}

	/// <summary>Reads three IEEE-754 floats into a Vector3.</summary>
	public static Vector3 GetVec3(this NetPacketReader r) =>
		new(r.GetFloat(), r.GetFloat(), r.GetFloat());

	/// <summary>16-bit cm-quantised Vec3 (6 B, range ±327.67 m, 1 cm precision). For snapshot Pos/Vel only —
	/// tracers/directions need full float precision.</summary>
	public static void PutVec3Quantized(this NetDataWriter w, Vector3 v)
	{
		w.Put((short)Mathf.Clamp(Mathf.RoundToInt(v.X * 100f), short.MinValue, short.MaxValue));
		w.Put((short)Mathf.Clamp(Mathf.RoundToInt(v.Y * 100f), short.MinValue, short.MaxValue));
		w.Put((short)Mathf.Clamp(Mathf.RoundToInt(v.Z * 100f), short.MinValue, short.MaxValue));
	}

	/// <summary>Reads a cm-quantised Vec3 emitted by <see cref="PutVec3Quantized"/>.</summary>
	public static Vector3 GetVec3Quantized(this NetPacketReader r) =>
		new(r.GetShort() / 100f, r.GetShort() / 100f, r.GetShort() / 100f);

	/// <summary>Writes a ConnectRequest packet with the player name and identity token.</summary>
	public static NetDataWriter WriteConnectRequest(string playerName, byte[] token)
	{
		var w = Begin(PacketType.ConnectRequest);
		w.Put(ProtocolVersion);
		w.Put(playerName ?? "Player");
		w.PutBytesWithLength(token ?? System.Array.Empty<byte>());
		return w;
	}

	/// <summary>Reads a ConnectRequest body into protocol version, player name, and identity token.</summary>
	public static void ReadConnectRequest(NetPacketReader r, out ushort proto, out string playerName, out byte[] token)
	{
		proto = r.GetUShort();
		playerName = r.GetString(64);
		token = r.GetBytesWithLength();
	}

	/// <summary>Writes a SpawnAck (joiner's NetId, world info, spawn pose, initial roster). In competitive mode
	/// <paramref name="yourTeam"/> is <see cref="Team.Spectator"/> and the pose is ignored until <see cref="PacketType.TeamSelect"/>.</summary>
	public static NetDataWriter WriteSpawnAck(
		byte yourNetId,
		Team yourTeam,
		string mapPath,
		uint serverTickNow,
		ushort tickRate,
		Vector3 spawnPos,
		float spawnYaw,
		System.Collections.Generic.IReadOnlyList<InitialPlayerState> otherPlayers,
		byte[] assignedToken)
	{
		var w = Begin(PacketType.SpawnAck);
		w.Put(yourNetId);
		w.Put((byte)yourTeam);
		w.Put(mapPath ?? "res://world.tscn");
		w.Put(serverTickNow);
		w.Put(tickRate);
		w.PutVec3(spawnPos);
		w.Put(spawnYaw);
		w.PutBytesWithLength(assignedToken ?? System.Array.Empty<byte>());
		w.Put((byte)otherPlayers.Count);
		foreach (var p in otherPlayers)
		{
			w.Put(p.NetId);
			w.Put(p.PlayerName ?? "");
			w.PutVec3(p.Position);
			w.Put(p.Yaw);
			w.Put(p.Hp);
			w.Put(p.ActiveSlot);
			w.Put(p.WeaponId);
			w.Put(p.Team);
			w.Put(p.TeamSlot);
		}
		return w;
	}

	/// <summary>Reads a SpawnAck body into out parameters, including the array of already-spawned players.</summary>
	public static void ReadSpawnAck(NetPacketReader r,
		out byte yourNetId, out Team yourTeam,
		out string mapPath, out uint serverTick, out ushort tickRate,
		out Vector3 spawnPos, out float spawnYaw,
		out InitialPlayerState[] others, out byte[] assignedToken)
	{
		yourNetId = r.GetByte();
		yourTeam = (Team)r.GetByte();
		mapPath = r.GetString(128);
		serverTick = r.GetUInt();
		tickRate = r.GetUShort();
		spawnPos = r.GetVec3();
		spawnYaw = r.GetFloat();
		assignedToken = r.GetBytesWithLength();
		int count = r.GetByte();
		others = new InitialPlayerState[count];
		for (int i = 0; i < count; i++)
		{
			others[i] = new InitialPlayerState
			{
				NetId = r.GetByte(),
				PlayerName = r.GetString(64),
				Position = r.GetVec3(),
				Yaw = r.GetFloat(),
				Hp = r.GetByte(),
				ActiveSlot = r.GetByte(),
				WeaponId = r.GetByte(),
				Team = r.GetByte(),
				TeamSlot = r.GetByte(),
			};
		}
	}

	/// <summary>Writes a RoundState packet (start tick, duration, round number, total). Clients derive
	/// <see cref="NetClient.RoundTimeRemainingSec"/> from it.</summary>
	public static NetDataWriter WriteRoundState(uint startTick, ushort durationSec, ushort roundNumber, ushort roundsTotal)
	{
		var w = Begin(PacketType.RoundState);
		w.Put(startTick);
		w.Put(durationSec);
		w.Put(roundNumber);
		w.Put(roundsTotal);
		return w;
	}

	/// <summary>Reads a RoundState packet payload into out params.</summary>
	public static void ReadRoundState(NetPacketReader r, out uint startTick, out ushort durationSec, out ushort roundNumber, out ushort roundsTotal)
	{
		startTick = r.GetUInt();
		durationSec = r.GetUShort();
		roundNumber = r.GetUShort();
		roundsTotal = r.GetUShort();
	}

	/// <summary>Writes a PlayerJoined packet announcing a new peer's NetId, name and initial state.</summary>
	public static NetDataWriter WritePlayerJoined(byte netId, string playerName, Vector3 spawnPos, float spawnYaw, byte hp, byte activeSlot, byte weaponId, byte team, byte teamSlot)
	{
		var w = Begin(PacketType.PlayerJoined);
		w.Put(netId);
		w.Put(playerName ?? "");
		w.PutVec3(spawnPos);
		w.Put(spawnYaw);
		w.Put(hp);
		w.Put(activeSlot);
		w.Put(weaponId);
		w.Put(team);
		w.Put(teamSlot);
		return w;
	}

	/// <summary>Reads a PlayerJoined body into an <see cref="InitialPlayerState"/>.</summary>
	public static InitialPlayerState ReadPlayerJoined(NetPacketReader r) => new()
	{
		NetId = r.GetByte(),
		PlayerName = r.GetString(64),
		Position = r.GetVec3(),
		Yaw = r.GetFloat(),
		Hp = r.GetByte(),
		ActiveSlot = r.GetByte(),
		WeaponId = r.GetByte(),
		Team = r.GetByte(),
		TeamSlot = r.GetByte(),
	};

	private const float HalfPi = Mathf.Pi * 0.5f;

	/// <summary>Quantises a yaw angle in radians to a ushort (range [-π..π] mapped to 0..65535).</summary>
	public static ushort QuantizeYaw(float radians)
	{
		float t = (Mathf.PosMod(radians, Mathf.Tau)) / Mathf.Tau;
		return (ushort)Mathf.Clamp(Mathf.RoundToInt(t * 65535f), 0, 65535);
	}
	/// <summary>Restores a yaw angle in radians from its ushort quantisation.</summary>
	public static float DequantizeYaw(ushort q) => (q / 65535f) * Mathf.Tau;

	/// <summary>Quantises a pitch angle in radians to a ushort (range [-π/2..π/2] mapped to 0..65535).</summary>
	public static ushort QuantizePitch(float radians)
	{
		float t = Mathf.Clamp((radians + HalfPi) / Mathf.Pi, 0f, 1f);
		return (ushort)Mathf.Clamp(Mathf.RoundToInt(t * 65535f), 0, 65535);
	}
	/// <summary>Restores a pitch angle in radians from its ushort quantisation.</summary>
	public static float DequantizePitch(ushort q) => (q / 65535f) * Mathf.Pi - HalfPi;

	/// <summary>Quantises + packs a sampled input into wire form, incl. subtick events from
	/// <see cref="MovementInput.Events"/>. EventCount is capped at <see cref="MaxSubtickEventsWire"/> (surplus
	/// dropped; held state stays correct). Optional <paramref name="eventBuffer"/> is a caller-owned scratch
	/// array reused to avoid a per-tick alloc; it must outlive the struct's stay in the redundancy ring.</summary>
	public static EncodedInput EncodeInput(uint tickIndex, in MovementInput mi,
		bool firePressed, bool reloadPressed, bool inspectPressed, bool slotIsGrenade,
		byte fireSubTick, SubtickEventEncoded[] eventBuffer = null)
	{
		byte f1 = 0;
		if (mi.SprintHeld)     f1 |= 1 << 0;
		if (mi.ShiftHeld)      f1 |= 1 << 1;
		if (mi.CrouchHeld)     f1 |= 1 << 2;
		if (mi.CrouchPressed)  f1 |= 1 << 3;
		if (mi.AdsHeld)        f1 |= 1 << 4;
		if (mi.BreathHoldHeld) f1 |= 1 << 5;
		if (mi.JumpPressed)    f1 |= 1 << 6;
		if (firePressed)       f1 |= 1 << 7;
		byte f2 = 0;
		if (reloadPressed)     f2 |= 1 << 0;
		if (inspectPressed)    f2 |= 1 << 1;
		if (slotIsGrenade)     f2 |= 1 << 2;

		int eventCount = mi.Events != null ? mi.Events.Length : 0;
		if (eventCount > MaxSubtickEventsWire) eventCount = MaxSubtickEventsWire;
		SubtickEventEncoded[] events = null;
		if (eventCount > 0)
		{
			// Reuse the caller's pooled buffer when it's large enough — only the first EventCount entries
			// are ever read (WriteInputBody loops on EventCount, not Length). Falls back to a fresh array
			// when no buffer is supplied (keeps the parameterless behaviour for any other caller).
			events = (eventBuffer != null && eventBuffer.Length >= eventCount)
				? eventBuffer
				: new SubtickEventEncoded[eventCount];
			for (int i = 0; i < eventCount; i++)
			{
				SubtickEvent e = mi.Events[i];
				events[i] = new SubtickEventEncoded
				{
					TQ = (byte)Mathf.Clamp(Mathf.RoundToInt(e.TFraction * 256f), 0, 255),
					StateAfter = (ushort)e.StateAfter,
					QYaw = QuantizeYaw(e.ViewYaw),
					QPitch = QuantizePitch(e.ViewPitch),
				};
			}
		}

		return new EncodedInput
		{
			TickIndex = tickIndex,
			QYaw = QuantizeYaw(mi.ViewYaw),
			QPitch = QuantizePitch(mi.ViewPitch),
			QWishX = (short)Mathf.Clamp(Mathf.RoundToInt(mi.WishDir.X * 32767f), -32768, 32767),
			QWishZ = (short)Mathf.Clamp(Mathf.RoundToInt(mi.WishDir.Z * 32767f), -32768, 32767),
			Flags1 = f1,
			Flags2 = f2,
			FireSubTick = firePressed ? fireSubTick : (byte)0,
			InitialBits = (ushort)mi.InitialBits,
			QInitialYaw = QuantizeYaw(mi.InitialViewYaw),
			QInitialPitch = QuantizePitch(mi.InitialViewPitch),
			EventCount = (byte)eventCount,
			Events = events,
		};
	}

	/// <summary>Writes a full input packet [type|count|ackedSnapshotTick|N×body]. ackedSnapshotTick is
	/// once-per-packet; inputs must be oldest→newest for sequential server dedupe.</summary>
	public static void WriteInputPacketInto(NetDataWriter w, uint ackedSnapshotTick,
		EncodedInput[] inputs, int oldestIndex, int count)
	{
		w.Reset();
		w.Put((byte)PacketType.Input);
		w.Put((byte)count);
		w.Put(ackedSnapshotTick);
		for (int i = 0; i < count; i++)
			WriteInputBody(w, inputs[oldestIndex + i]);
	}

	private static void WriteInputBody(NetDataWriter w, in EncodedInput e)
	{
		w.Put(e.TickIndex);
		w.Put(e.QYaw);
		w.Put(e.QPitch);
		w.Put(e.QWishX);
		w.Put(e.QWishZ);
		w.Put(e.Flags1);
		w.Put(e.Flags2);
		w.Put(e.FireSubTick);
		w.Put(e.InitialBits);
		w.Put(e.QInitialYaw);
		w.Put(e.QInitialPitch);
		w.Put(e.EventCount);
		for (int i = 0; i < e.EventCount; i++)
		{
			SubtickEventEncoded ev = e.Events[i];
			w.Put(ev.TQ);
			w.Put(ev.StateAfter);
			w.Put(ev.QYaw);
			w.Put(ev.QPitch);
		}
	}

	/// <summary>Reads the input-packet header (count + ackedSnapshotTick); caller then loops <see cref="ReadInputBody"/>.</summary>
	public static void ReadInputHeader(NetPacketReader r, out byte count, out uint ackedSnapshotTick)
	{
		count = r.GetByte();
		ackedSnapshotTick = r.GetUInt();
	}

	/// <summary>Reads one input body (single client tick). EventCount is clamped to <see cref="MaxSubtickEventsWire"/>
	/// (cheat guard); a monotonic-violation event list is dropped (see below).</summary>
	public static void ReadInputBody(NetPacketReader r, out InputPacket pkt)
	{
		pkt = default;
		pkt.TickIndex = r.GetUInt();
		pkt.ViewYaw = DequantizeYaw(r.GetUShort());
		pkt.ViewPitch = DequantizePitch(r.GetUShort());
		pkt.WishX = r.GetShort() / 32767f;
		pkt.WishZ = r.GetShort() / 32767f;
		byte f1 = r.GetByte();
		pkt.SprintHeld     = (f1 & (1 << 0)) != 0;
		pkt.ShiftHeld      = (f1 & (1 << 1)) != 0;
		pkt.CrouchHeld     = (f1 & (1 << 2)) != 0;
		pkt.CrouchPressed  = (f1 & (1 << 3)) != 0;
		pkt.AdsHeld        = (f1 & (1 << 4)) != 0;
		pkt.BreathHoldHeld = (f1 & (1 << 5)) != 0;
		pkt.JumpPressed    = (f1 & (1 << 6)) != 0;
		pkt.FirePressed    = (f1 & (1 << 7)) != 0;
		byte f2 = r.GetByte();
		pkt.ReloadPressed  = (f2 & (1 << 0)) != 0;
		pkt.InspectPressed = (f2 & (1 << 1)) != 0;
		pkt.SlotIsGrenade  = (f2 & (1 << 2)) != 0;
		pkt.FireSubTick    = r.GetByte();

		pkt.InitialBits = r.GetUShort();
		pkt.InitialViewYaw = DequantizeYaw(r.GetUShort());
		pkt.InitialViewPitch = DequantizePitch(r.GetUShort());
		int wireCount = r.GetByte();
		int eventCount = wireCount > MaxSubtickEventsWire ? MaxSubtickEventsWire : wireCount;
		if (eventCount > 0)
		{
			pkt.Events = new SubtickEvent[eventCount];
			byte lastTQ = 0;
			bool monotonicViolation = false;
			for (int i = 0; i < eventCount; i++)
			{
				byte tq = r.GetByte();
				ushort state = r.GetUShort();
				ushort qYaw = r.GetUShort();
				ushort qPitch = r.GetUShort();
				if (i > 0 && tq < lastTQ) monotonicViolation = true;
				lastTQ = tq;
				pkt.Events[i] = new SubtickEvent
				{
					TFraction = tq / 256f,
					StateAfter = (InputBits)state,
					ViewYaw = DequantizeYaw(qYaw),
					ViewPitch = DequantizePitch(qPitch),
				};
			}
			// Skip the leftover bytes from a wire-capped overflow so the cursor lines up with the next body.
			for (int skip = eventCount; skip < wireCount; skip++)
			{
				r.GetByte(); r.GetUShort(); r.GetUShort(); r.GetUShort();
			}
			// Drop the whole event list on monotonic violation — a malicious client could otherwise rewind
			// the substep state by sending events out of order. Held-state at end-of-tick (legacy fields)
			// is still consumed via the fast path, so the player still moves correctly that tick.
			if (monotonicViolation) pkt.Events = null;
		}
	}

	/// <summary>Writes a delta-baseline-compressed snapshot. <paramref name="baselineTick"/> = <see cref="NoBaselineTick"/>
	/// forces a full snapshot (mask = All); otherwise each player is delta'd against the matching baseline entry.
	/// Caller must pass a baseline from the same PVS view.</summary>
	public static void WriteSnapshotInto(NetDataWriter w, uint serverTick, uint ackedInputTick,
		uint baselineTick,
		System.Collections.Generic.IReadOnlyList<SnapshotPlayer> players,
		SnapshotPlayer[] baselinePlayers, int baselineCount)
	{
		w.Put((byte)PacketType.Snapshot);
		w.Put(serverTick);
		w.Put(ackedInputTick);
		w.Put(baselineTick);
		w.Put((byte)players.Count);
		bool hasBaseline = baselineTick != NoBaselineTick && baselinePlayers != null && baselineCount > 0;
		for (int i = 0; i < players.Count; i++)
		{
			var cur = players[i];
			SnapshotFieldFlags mask;
			if (!hasBaseline || !TryFindBaselinePlayer(baselinePlayers, baselineCount, cur.NetId, out var baseline))
			{
				mask = SnapshotFieldFlags.All;
			}
			else
			{
				mask = ComputeFieldMask(in cur, in baseline);
			}
			w.Put(cur.NetId);
			w.Put((ushort)mask);
			if ((mask & SnapshotFieldFlags.Flags) != 0) w.Put(cur.Flags);
			if ((mask & SnapshotFieldFlags.Movement) != 0) { w.PutVec3Quantized(cur.Pos); w.PutVec3Quantized(cur.Vel); }
			if ((mask & SnapshotFieldFlags.View) != 0) { w.Put(QuantizeYaw(cur.Yaw)); w.Put(QuantizePitch(cur.Pitch)); }
			if ((mask & SnapshotFieldFlags.Blends) != 0) { w.Put(cur.AdsBlend); w.Put(cur.CrouchBlend); w.Put(cur.RaiseBlend); }
			if ((mask & SnapshotFieldFlags.ShotIndex) != 0) w.Put(cur.ShotIndex);
			if ((mask & SnapshotFieldFlags.Hp) != 0) w.Put(cur.Hp);
			if ((mask & SnapshotFieldFlags.Armor) != 0) w.Put(cur.Armor);
			if ((mask & SnapshotFieldFlags.Weapon) != 0) { w.Put(cur.ActiveSlot); w.Put(cur.WeaponId); }
			if ((mask & SnapshotFieldFlags.AimPunch) != 0) { w.Put(cur.AimPunchX); w.Put(cur.AimPunchY); }
			if ((mask & SnapshotFieldFlags.Footstep) != 0) w.Put(cur.FootstepPhase);
			if ((mask & SnapshotFieldFlags.Score) != 0) { w.Put(cur.Kills); w.Put(cur.Deaths); }
			if ((mask & SnapshotFieldFlags.Ping) != 0) w.Put(cur.PingMs);
			if ((mask & SnapshotFieldFlags.Team) != 0) { w.Put(cur.Team); w.Put(cur.TeamSlot); }
		}
	}

	/// <summary>Returns the bitmask of field groups that changed between <paramref name="cur"/> and
	/// <paramref name="baseline"/>. Per-player hot path — struct-by-ref, no LINQ.</summary>
	private static SnapshotFieldFlags ComputeFieldMask(in SnapshotPlayer cur, in SnapshotPlayer baseline)
	{
		SnapshotFieldFlags m = SnapshotFieldFlags.None;
		if (cur.Flags != baseline.Flags) m |= SnapshotFieldFlags.Flags;
		if (cur.Pos != baseline.Pos || cur.Vel != baseline.Vel) m |= SnapshotFieldFlags.Movement;
		// Compare view on the QUANTISED values — otherwise float noise on yaw/pitch resends constantly even
		// though the wire bytes are identical. Saves ~4 B/player/tick while aiming idle.
		if (QuantizeYaw(cur.Yaw) != QuantizeYaw(baseline.Yaw) || QuantizePitch(cur.Pitch) != QuantizePitch(baseline.Pitch))
			m |= SnapshotFieldFlags.View;
		if (cur.AdsBlend != baseline.AdsBlend || cur.CrouchBlend != baseline.CrouchBlend || cur.RaiseBlend != baseline.RaiseBlend)
			m |= SnapshotFieldFlags.Blends;
		if (cur.ShotIndex != baseline.ShotIndex) m |= SnapshotFieldFlags.ShotIndex;
		if (cur.Hp != baseline.Hp) m |= SnapshotFieldFlags.Hp;
		if (cur.Armor != baseline.Armor) m |= SnapshotFieldFlags.Armor;
		if (cur.ActiveSlot != baseline.ActiveSlot || cur.WeaponId != baseline.WeaponId) m |= SnapshotFieldFlags.Weapon;
		if (cur.AimPunchX != baseline.AimPunchX || cur.AimPunchY != baseline.AimPunchY) m |= SnapshotFieldFlags.AimPunch;
		if (cur.FootstepPhase != baseline.FootstepPhase) m |= SnapshotFieldFlags.Footstep;
		if (cur.Kills != baseline.Kills || cur.Deaths != baseline.Deaths) m |= SnapshotFieldFlags.Score;
		if (cur.PingMs != baseline.PingMs) m |= SnapshotFieldFlags.Ping;
		if (cur.Team != baseline.Team || cur.TeamSlot != baseline.TeamSlot) m |= SnapshotFieldFlags.Team;
		return m;
	}

	/// <summary>Linear NetId lookup over the baseline players (n ≤ 16, so no dictionary).</summary>
	private static bool TryFindBaselinePlayer(SnapshotPlayer[] baseline, int count, byte netId, out SnapshotPlayer found)
	{
		for (int i = 0; i < count; i++)
		{
			if (baseline[i].NetId == netId) { found = baseline[i]; return true; }
		}
		found = default;
		return false;
	}

	/// <summary>Reads a delta-snapshot packet via a caller-supplied baseline lookup (tick → players, or null).
	/// Full snapshot when <c>baselineTick == <see cref="NoBaselineTick"/></c>. Returns false (drop + don't ack)
	/// if a needed baseline is missing.</summary>
	public static bool ReadSnapshot(NetPacketReader r, out uint serverTick, out uint ackedInputTick,
		out uint baselineTick,
		System.Func<uint, (SnapshotPlayer[] players, int count)?> baselineLookup,
		ref SnapshotPlayer[] buffer, out int playerCount)
	{
		serverTick = r.GetUInt();
		ackedInputTick = r.GetUInt();
		baselineTick = r.GetUInt();
		playerCount = r.GetByte();

		SnapshotPlayer[] basePlayers = null;
		int baseCount = 0;
		if (baselineTick != NoBaselineTick)
		{
			var lookup = baselineLookup?.Invoke(baselineTick);
			if (!lookup.HasValue)
				return false; // Baseline aged out — drop the packet. The client keeps its LastReceivedSnapshotTick;
				              // the server either delta's against another baseline tick or eventually ages to a
				              // full snapshot → self-healing.
			basePlayers = lookup.Value.players;
			baseCount = lookup.Value.count;
		}

		if (buffer == null || buffer.Length < playerCount) buffer = new SnapshotPlayer[playerCount];
		for (int i = 0; i < playerCount; i++)
		{
			byte netId = r.GetByte();
			var mask = (SnapshotFieldFlags)r.GetUShort();
			SnapshotPlayer p;
			if (basePlayers != null && TryFindBaselinePlayer(basePlayers, baseCount, netId, out var baseline))
				p = baseline;
			else
				p = default;
			p.NetId = netId;
			if ((mask & SnapshotFieldFlags.Flags) != 0) p.Flags = r.GetByte();
			if ((mask & SnapshotFieldFlags.Movement) != 0) { p.Pos = r.GetVec3Quantized(); p.Vel = r.GetVec3Quantized(); }
			if ((mask & SnapshotFieldFlags.View) != 0) { p.Yaw = DequantizeYaw(r.GetUShort()); p.Pitch = DequantizePitch(r.GetUShort()); }
			if ((mask & SnapshotFieldFlags.Blends) != 0) { p.AdsBlend = r.GetByte(); p.CrouchBlend = r.GetByte(); p.RaiseBlend = r.GetByte(); }
			if ((mask & SnapshotFieldFlags.ShotIndex) != 0) p.ShotIndex = r.GetUShort();
			if ((mask & SnapshotFieldFlags.Hp) != 0) p.Hp = r.GetByte();
			if ((mask & SnapshotFieldFlags.Armor) != 0) p.Armor = r.GetByte();
			if ((mask & SnapshotFieldFlags.Weapon) != 0) { p.ActiveSlot = r.GetByte(); p.WeaponId = r.GetByte(); }
			if ((mask & SnapshotFieldFlags.AimPunch) != 0) { p.AimPunchX = r.GetSByte(); p.AimPunchY = r.GetSByte(); }
			if ((mask & SnapshotFieldFlags.Footstep) != 0) p.FootstepPhase = r.GetUShort();
			if ((mask & SnapshotFieldFlags.Score) != 0) { p.Kills = r.GetByte(); p.Deaths = r.GetByte(); }
			if ((mask & SnapshotFieldFlags.Ping) != 0) p.PingMs = r.GetByte();
			if ((mask & SnapshotFieldFlags.Team) != 0) { p.Team = r.GetByte(); p.TeamSlot = r.GetByte(); }
			buffer[i] = p;
		}
		return true;
	}

/// <summary>Writes a ShotFired packet — origin, direction and optional authoritative hit data.</summary>
	public static NetDataWriter WriteShotFired(byte netId, byte weaponId, Vector3 origin, Vector3 dir,
		bool tracer, bool hit, Vector3 hitPos, Vector3 hitNormal, string material)
	{
		var w = Begin(PacketType.ShotFired);
		w.Put(netId);
		w.Put(weaponId);
		w.PutVec3(origin);
		w.PutVec3(dir);
		byte flags = 0;
		if (tracer) flags |= 1 << 0;
		if (hit)    flags |= 1 << 1;
		w.Put(flags);
		if (hit)
		{
			w.PutVec3(hitPos);
			w.PutVec3(hitNormal);
			w.Put(MaterialToId(material));
		}
		return w;
	}

	/// <summary>Reads a ShotFired packet, including the optional hit position/normal/material when present.</summary>
	public static void ReadShotFired(NetPacketReader r, out byte netId, out byte weaponId,
		out Vector3 origin, out Vector3 dir, out bool tracer, out bool hit,
		out Vector3 hitPos, out Vector3 hitNormal, out string material)
	{
		netId = r.GetByte();
		weaponId = r.GetByte();
		origin = r.GetVec3();
		dir = r.GetVec3();
		byte flags = r.GetByte();
		tracer = (flags & (1 << 0)) != 0;
		hit = (flags & (1 << 1)) != 0;
		if (hit)
		{
			hitPos = r.GetVec3();
			hitNormal = r.GetVec3();
			material = IdToMaterial(r.GetByte());
		}
		else
		{
			hitPos = default;
			hitNormal = default;
			material = "default";
		}
	}

	/// <summary>Writes a Hit packet (shooter, victim, hitbox group, damage, hp left, weapon).</summary>
	public static NetDataWriter WriteHit(byte shooterNetId, byte victimNetId, HitboxGroup group, byte damage, byte hpLeft, byte weaponId)
	{
		var w = Begin(PacketType.Hit);
		w.Put(shooterNetId);
		w.Put(victimNetId);
		w.Put((byte)group);
		w.Put(damage);
		w.Put(hpLeft);
		w.Put(weaponId);
		return w;
	}

	/// <summary>Reads a Hit packet into out parameters describing the shooter, victim and damage details.</summary>
	public static void ReadHit(NetPacketReader r, out byte shooterNetId, out byte victimNetId,
		out HitboxGroup group, out byte damage, out byte hpLeft, out byte weaponId)
	{
		shooterNetId = r.GetByte();
		victimNetId = r.GetByte();
		group = (HitboxGroup)r.GetByte();
		damage = r.GetByte();
		hpLeft = r.GetByte();
		weaponId = r.GetByte();
	}

	/// <summary>Writes a Footstep packet with position, material id, loudness and flags.</summary>
	public static NetDataWriter WriteFootstep(byte netId, Vector3 pos, string material, byte loudness, bool leftFoot, bool sprinting)
	{
		var w = Begin(PacketType.Footstep);
		w.Put(netId);
		w.PutVec3(pos);
		w.Put(MaterialToId(material));
		w.Put(loudness);
		byte flags = 0;
		if (leftFoot)  flags |= 1 << 0;
		if (sprinting) flags |= 1 << 1;
		w.Put(flags);
		return w;
	}

	/// <summary>Reads a Footstep packet into out parameters and resolves the material id to a name.</summary>
	public static void ReadFootstep(NetPacketReader r, out byte netId, out Vector3 pos, out string material,
		out byte loudness, out bool leftFoot, out bool sprinting)
	{
		netId = r.GetByte();
		pos = r.GetVec3();
		material = IdToMaterial(r.GetByte());
		loudness = r.GetByte();
		byte flags = r.GetByte();
		leftFoot = (flags & (1 << 0)) != 0;
		sprinting = (flags & (1 << 1)) != 0;
	}

	/// <summary>Writes a Respawn packet with the new pose and HP for the respawning player.</summary>
	public static NetDataWriter WriteRespawn(byte netId, Vector3 pos, float yaw, byte hp)
	{
		var w = Begin(PacketType.Respawn);
		w.Put(netId);
		w.PutVec3(pos);
		w.Put(yaw);
		w.Put(hp);
		return w;
	}
	/// <summary>Reads a Respawn packet into pose and HP out parameters.</summary>
	public static void ReadRespawn(NetPacketReader r, out byte netId, out Vector3 pos, out float yaw, out byte hp)
	{
		netId = r.GetByte();
		pos = r.GetVec3();
		yaw = r.GetFloat();
		hp = r.GetByte();
	}

	/// <summary>Writes a Death packet (victim, attacker, weaponId, headshot flag). weaponId = 0 for world damage.</summary>
	public static NetDataWriter WriteDeath(byte victimNetId, byte attackerNetId, byte weaponId, bool isHeadshot)
	{
		var w = Begin(PacketType.Death);
		w.Put(victimNetId);
		w.Put(attackerNetId);
		w.Put(weaponId);
		w.Put(isHeadshot);
		return w;
	}
	/// <summary>Reads a Death packet into victim + attacker + weaponId + headshot-flag.</summary>
	public static void ReadDeath(NetPacketReader r, out byte victimNetId, out byte attackerNetId, out byte weaponId, out bool isHeadshot)
	{
		victimNetId = r.GetByte();
		attackerNetId = r.GetByte();
		weaponId = r.GetByte();
		isHeadshot = r.GetBool();
	}

	/// <summary>Writes a Jump packet carrying just the jumper's NetId.</summary>
	public static NetDataWriter WriteJump(byte netId) { var w = Begin(PacketType.Jump); w.Put(netId); return w; }
	/// <summary>Reads a Jump packet's NetId.</summary>
	public static void ReadJump(NetPacketReader r, out byte netId) { netId = r.GetByte(); }

	/// <summary>Writes a DropMag packet carrying just the reloading player's NetId.</summary>
	public static NetDataWriter WriteDropMag(byte netId) { var w = Begin(PacketType.DropMag); w.Put(netId); return w; }
	/// <summary>Reads a DropMag packet's NetId.</summary>
	public static void ReadDropMag(NetPacketReader r, out byte netId) { netId = r.GetByte(); }

	/// <summary>Writes a Land packet with the landing player's NetId and impact speed.</summary>
	public static NetDataWriter WriteLand(byte netId, float impactSpeed)
	{
		var w = Begin(PacketType.Land);
		w.Put(netId);
		w.Put(impactSpeed);
		return w;
	}
	/// <summary>Reads a Land packet into NetId and impact-speed out parameters.</summary>
	public static void ReadLand(NetPacketReader r, out byte netId, out float impactSpeed)
	{
		netId = r.GetByte();
		impactSpeed = r.GetFloat();
	}

	/// <summary>Writes a GrenadeSpawn packet with owner/projectile ids, type, origin and velocity.</summary>
	public static NetDataWriter WriteGrenadeSpawn(byte netId, uint projectileId, byte grenadeType, Vector3 origin, Vector3 velocity)
	{
		var w = Begin(PacketType.GrenadeSpawn);
		w.Put(netId);
		w.Put(projectileId);
		w.Put(grenadeType);
		w.PutVec3(origin);
		w.PutVec3(velocity);
		return w;
	}

	/// <summary>Reads a GrenadeSpawn packet into owner/projectile ids and motion data.</summary>
	public static void ReadGrenadeSpawn(NetPacketReader r, out byte netId, out uint projectileId,
		out byte grenadeType, out Vector3 origin, out Vector3 velocity)
	{
		netId = r.GetByte();
		projectileId = r.GetUInt();
		grenadeType = r.GetByte();
		origin = r.GetVec3();
		velocity = r.GetVec3();
	}

	/// <summary>Writes a ProjectileState packet with cm-quantised position and velocity.</summary>
	public static NetDataWriter WriteProjectileState(byte ownerNetId, uint projectileId, Vector3 pos, Vector3 vel)
	{
		var w = Begin(PacketType.ProjectileState);
		w.Put(ownerNetId);
		w.Put(projectileId);
		w.PutVec3Quantized(pos);
		w.PutVec3Quantized(vel);
		return w;
	}

	/// <summary>Reads a ProjectileState packet's owner, projectile id and dequantised pose data.</summary>
	public static void ReadProjectileState(NetPacketReader r, out byte ownerNetId, out uint projectileId,
		out Vector3 pos, out Vector3 vel)
	{
		ownerNetId = r.GetByte();
		projectileId = r.GetUInt();
		pos = r.GetVec3Quantized();
		vel = r.GetVec3Quantized();
	}

	/// <summary>Writes a ProjectileDespawn packet carrying the final resting position.</summary>
	public static NetDataWriter WriteProjectileDespawn(byte ownerNetId, uint projectileId, Vector3 finalPos)
	{
		var w = Begin(PacketType.ProjectileDespawn);
		w.Put(ownerNetId);
		w.Put(projectileId);
		w.PutVec3(finalPos);
		return w;
	}

	/// <summary>Reads a ProjectileDespawn packet into owner, projectile id and final position.</summary>
	public static void ReadProjectileDespawn(NetPacketReader r, out byte ownerNetId, out uint projectileId, out Vector3 finalPos)
	{
		ownerNetId = r.GetByte();
		projectileId = r.GetUInt();
		finalPos = r.GetVec3();
	}

	/// <summary>Writes a PlayerLeft packet with the NetId of the leaver and a reason byte.</summary>
	public static NetDataWriter WritePlayerLeft(byte netId, byte reason)
	{
		var w = Begin(PacketType.PlayerLeft);
		w.Put(netId);
		w.Put(reason);
		return w;
	}

	/// <summary>Reads a PlayerLeft packet into NetId and reason out parameters.</summary>
	public static void ReadPlayerLeft(NetPacketReader r, out byte netId, out byte reason)
	{
		netId = r.GetByte();
		reason = r.GetByte();
	}

	// ConVarSync (bidirectional, reliable):
	//   C2S request:   "set sv_debug_hitboxes 1" — server validates (sv_* whitelist), applies to ConVars.Sv,
	//                  then broadcasts to all clients.
	//   S2C broadcast: "sv_debug_hitboxes 1" — clients apply it locally so their visualisation gates on
	//                  ConVars.Sv.* stay in sync.
	public static NetDataWriter WriteConVarSyncRequest(string name, string value)
	{
		var w = Begin(PacketType.ConVarSyncRequest);
		w.Put(name ?? "");
		w.Put(value ?? "");
		return w;
	}

	/// <summary>Empty payload — the type byte alone flips WorldReady on the peer.</summary>
	public static NetDataWriter WriteWorldInitComplete() => Begin(PacketType.WorldInitComplete);

	/// <summary>C2S team choice (CT/T); Spectator is invalid. Server replies with SpawnAuthorize.</summary>
	public static NetDataWriter WriteTeamSelect(Team team)
	{
		var w = Begin(PacketType.TeamSelect);
		w.Put((byte)team);
		return w;
	}

	public static Team ReadTeamSelect(NetPacketReader r) => (Team)r.GetByte();

	/// <summary>S2C spawn grant after TeamSelect, carrying the resolved Team (may differ if rebalanced) and pose.</summary>
	public static NetDataWriter WriteSpawnAuthorize(Team team, Vector3 spawnPos, float spawnYaw)
	{
		var w = Begin(PacketType.SpawnAuthorize);
		w.Put((byte)team);
		w.Put(spawnPos.X); w.Put(spawnPos.Y); w.Put(spawnPos.Z);
		w.Put(spawnYaw);
		return w;
	}

	public static void ReadSpawnAuthorize(NetPacketReader r, out Team team, out Vector3 spawnPos, out float spawnYaw)
	{
		team = (Team)r.GetByte();
		spawnPos = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
		spawnYaw = r.GetFloat();
	}

	public static void ReadConVarSyncRequest(NetPacketReader r, out string name, out string value)
	{
		name = r.GetString(64);
		value = r.GetString(64);
	}

	public static NetDataWriter WriteConVarSyncBroadcast(string name, string value)
	{
		var w = Begin(PacketType.ConVarSyncBroadcast);
		w.Put(name ?? "");
		w.Put(value ?? "");
		return w;
	}

	public static void ReadConVarSyncBroadcast(NetPacketReader r, out string name, out string value)
	{
		name = r.GetString(64);
		value = r.GetString(64);
	}

	// DebugHitboxes (S2C unreliable, ~10 Hz, gated on the server): per agent, netId + hitboxCount + a list of
	// cm-quantised hitbox transforms. Kept deliberately small (~92 B/agent ≈ 15 KB/s extra at 16 players).
	// The client renders red spheres at each position via HudServerHitboxesDebug.

	/// <summary>One agent per packet (~640 B), under LiteNetLib's 1023 B unreliable MTU.</summary>
	public static NetDataWriter WriteDebugHitboxes(uint serverTick, in DebugHitboxAgent agent)
	{
		var w = Begin(PacketType.DebugHitboxes);
		w.Put(serverTick);
		w.Put(agent.NetId);
		w.Put((byte)agent.Transforms.Length);
		for (int i = 0; i < agent.Transforms.Length; i++)
		{
			var t = agent.Transforms[i];
			w.PutVec3Quantized(t.Origin);
			// Full basis as 3 Vec3 (36 B) INCLUDING scale. Quaternion-only was a bug: it lost the
			// tps_character skeleton scale (0.01), so the client rendered at scale=1 and a 28-unit capsule
			// radius became 28 metres instead of 28 cm.
			w.PutVec3(t.Basis.X);
			w.PutVec3(t.Basis.Y);
			w.PutVec3(t.Basis.Z);
		}
		return w;
	}

	public static DebugHitboxAgent ReadDebugHitboxes(NetPacketReader r, out uint serverTick)
	{
		serverTick = r.GetUInt();
		byte netId = r.GetByte();
		int hbCount = r.GetByte();
		var transforms = new Transform3D[hbCount];
		for (int i = 0; i < hbCount; i++)
		{
			Vector3 origin = r.GetVec3Quantized();
			Vector3 bx = r.GetVec3();
			Vector3 by = r.GetVec3();
			Vector3 bz = r.GetVec3();
			transforms[i] = new Transform3D(new Basis(bx, by, bz), origin);
		}
		return new DebugHitboxAgent { NetId = netId, Transforms = transforms };
	}

	/// <summary>Writes a ServerLog packet — a UTF-8 string the client prints to its own log.</summary>
	public static NetDataWriter WriteServerLog(string message)
	{
		var w = Begin(PacketType.ServerLog);
		w.Put(message ?? "");
		return w;
	}

	/// <summary>Reads a ServerLog string (capped 512 chars to bound a rogue server's bandwidth).</summary>
	public static void ReadServerLog(NetPacketReader r, out string message)
	{
		message = r.GetString(512);
	}
}
