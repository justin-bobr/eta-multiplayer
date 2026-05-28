using Godot;
using LiteNetLib;
using LiteNetLib.Utils;

/// <summary>
/// Central packet types and read/write helpers. Each packet starts with a <see cref="PacketType"/>
/// byte; a type-specific body follows.
///
/// Delivery method distribution:
///   - C2S Input + S2C Snapshot:        Unreliable (channel 0) — drops are fine, stale values
///                                      are simply discarded.
///   - All gameplay-relevant events:    ReliableOrdered (channel 1) — Shot/Reload/Footstep etc.
///                                      must arrive in order.
///   - ConnectRequest + SpawnAck:       ReliableOrdered (channel 1) — handshake.
///
/// Token format: variable-length byte array so a 16-byte GUID for v1 and external auth tokens
/// (variable, ~64 bytes) for v2 both fit without breaking the packet format.
/// </summary>
public static class Packets
{
	/// <summary>Current protocol version. Bump when the wire format changes incompatibly.
	/// v2: Snapshot Pos/Vel are cm-quantised int16 (instead of float Vec3). Material in
	/// ShotFired/Footstep is a byte id instead of a string (see <see cref="MaterialNames"/>).
	/// v3: Delta-Baseline Snapshot Compression — Snapshot trägt jetzt baselineTick + per-Player
	/// fieldMask, schickt nur veränderte Felder. Input-Packet trägt ackedSnapshotTick (uint,
	/// vorher byte ackDelta) damit Server die letzte ACK'd Baseline pro Peer kennt.
	/// v4: Input-Redundancy — Input-Packet trägt jetzt inputCount + ackedSnapshotTick (hoisted
	/// aus dem Body) + N input bodies. Client sendet die letzten <see cref="MaxInputRedundancy"/>
	/// Inputs in jedem Packet damit Single-Packet-Loss keine Edge-triggered Intents (Jump, Reload)
	/// killt. Server dedupliziert per tickIndex.
	/// v5: Subtick fire-timing — Input body trägt zusätzlich 1 Byte FireSubTick (Quantisierung
	/// 1/256-Tick = ~30µs bei 128Hz), das den Sub-Tick-Zeitpunkt des Fire-Press-Edges encoded.
	/// Server-Hitscan rewindt Lag-Comp auf fraktionalen Tick → eliminiert tick-aliasing bei Duellen.</summary>
	public const ushort ProtocolVersion = 5;

	/// <summary>Maximale Anzahl Inputs die in einem Input-Packet redundant gebündelt sind. 3 deckt
	/// 2 aufeinanderfolgende Packet-Drops ab. Mehr bringt diminishing returns + frisst MTU.</summary>
	public const int MaxInputRedundancy = 3;

	/// <summary>Sentinel-Tick für "keine Baseline" — Snapshot ist ein Full-Send, oder Client hat
	/// noch nichts empfangen. Server beginnt bei Tick 1 damit Tick 0 nie ein valider Snapshot-Tick
	/// ist und der Sentinel niemals mit einem echten Tick kollidiert.</summary>
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

	/// <summary>16-bit cm-quantised Vec3 (6 bytes vs 12 bytes for float Vec3). Range ±327.67 m —
	/// enough for any map size ≤ 600 m. 1 cm precision — puppet interpolation hides the quantisation
	/// completely. Only intended for snapshot Pos/Vel (tracers/directions need sub-cm/normalised precision).</summary>
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

	/// <summary>Writes a SpawnAck packet with the joiner's NetId, world info, spawn pose and initial player roster.</summary>
	public static NetDataWriter WriteSpawnAck(
		byte yourNetId,
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
		out byte yourNetId, out string mapPath, out uint serverTick, out ushort tickRate,
		out Vector3 spawnPos, out float spawnYaw,
		out InitialPlayerState[] others, out byte[] assignedToken)
	{
		yourNetId = r.GetByte();
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

	/// <summary>Writes a RoundState packet — broadcasts the current round's start tick, duration in
	/// seconds, current round number and total rounds for the match. Sent at 1Hz heartbeat + on each
	/// round transition. Clients derive <see cref="NetClient.RoundTimeRemainingSec"/> from this.</summary>
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

	/// <summary>Pre-quantisierte Form eines client-erzeugten Input-Frames für den Redundancy-Ring auf
	/// NetClient. Quantisierung passiert beim Pushen (einmal), nicht beim Senden (3x bei voller
	/// Redundancy). Spart pro Tick ~5 µs CPU.</summary>
	public struct EncodedInput
	{
		public uint TickIndex;
		public ushort QYaw;
		public ushort QPitch;
		public short QWishX;
		public short QWishZ;
		public byte Flags1;
		public byte Flags2;
		/// <summary>Sub-tick fire-press offset (0..255 → 0..0.996 of a tick). Only meaningful when
		/// <see cref="Flags1"/> bit 7 (firePressed) is set; otherwise 0. Captures the wallclock fraction
		/// at which the fire-press edge occurred within the client's current tick, so the server can
		/// rewind lag-comp to a fractional tick instead of snapping to a tick boundary.</summary>
		public byte FireSubTick;
	}

	/// <summary>Quantisiert + verpackt einen frisch gesampelten Input in eine wire-ready Form.</summary>
	public static EncodedInput EncodeInput(uint tickIndex, in MovementInput mi,
		bool firePressed, bool reloadPressed, bool inspectPressed, bool slotIsGrenade,
		byte fireSubTick)
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
		};
	}

	/// <summary>Schreibt ein komplettes Input-Packet mit Header + N Input-Bodies in einen pre-
	/// allokierten Writer. Layout: [type|count|ackedSnapshotTick|N×body]. ackedSnapshotTick ist
	/// einmal-pro-Packet (gilt für alle Inputs, der Wert ist sowieso identisch). Inputs werden in
	/// chronologischer Reihenfolge (oldest → newest) erwartet damit der Server sequenziell
	/// deduplizieren kann.</summary>
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
	}

	/// <summary>Reads the input-packet header (count + ackedSnapshotTick). Caller iteriert dann
	/// <paramref name="count"/>× <see cref="ReadInputBody"/>.</summary>
	public static void ReadInputHeader(NetPacketReader r, out byte count, out uint ackedSnapshotTick)
	{
		count = r.GetByte();
		ackedSnapshotTick = r.GetUInt();
	}

	/// <summary>Reads one input body (single client tick) into a fresh <see cref="InputPacket"/>.</summary>
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
	}

	/// <summary>Schreibt ein Snapshot-Packet mit Delta-Baseline-Compression in einen pre-allokierten
	/// Writer. <paramref name="baselineTick"/> = <see cref="NoBaselineTick"/> erzwingt Full-Snapshot
	/// (alle Player-Blocks mit Mask=All); sonst wird per Player gegen den passenden Eintrag in
	/// <paramref name="baselinePlayers"/> deltat (nur veränderte Feldgruppen on-wire). Caller sorgt
	/// dafür dass <paramref name="baselinePlayers"/>/<paramref name="baselineCount"/> der tatsächlichen
	/// Baseline (gleiche PVS-Sicht) entspricht.</summary>
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

	/// <summary>Vergleicht alle Felder von <paramref name="cur"/> gegen <paramref name="baseline"/> und
	/// returnt die Bitmaske der Feldgruppen die sich geändert haben. Aufgerufen pro Player pro Snapshot
	/// auf dem Server — Hot-Path, daher kein LINQ + struct-by-ref.</summary>
	private static SnapshotFieldFlags ComputeFieldMask(in SnapshotPlayer cur, in SnapshotPlayer baseline)
	{
		SnapshotFieldFlags m = SnapshotFieldFlags.None;
		if (cur.Flags != baseline.Flags) m |= SnapshotFieldFlags.Flags;
		if (cur.Pos != baseline.Pos || cur.Vel != baseline.Vel) m |= SnapshotFieldFlags.Movement;
		// View vergleichen auf den QUANTISIERTEN Werten — sonst senden wir ständig wegen float-noise
		// auf Yaw/Pitch obwohl on-wire dasselbe rauskommt. Spart auf idle-Aim ~4 Byte/Player/Tick.
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

	/// <summary>Linear-Scan über die Baseline-Player für NetId-Lookup. n≤16 in der Praxis — kein
	/// Bedarf für Dictionary mit dem Allocation-Overhead.</summary>
	private static bool TryFindBaselinePlayer(SnapshotPlayer[] baseline, int count, byte netId, out SnapshotPlayer found)
	{
		for (int i = 0; i < count; i++)
		{
			if (baseline[i].NetId == netId) { found = baseline[i]; return true; }
		}
		found = default;
		return false;
	}

	/// <summary>Liest ein Delta-Snapshot-Packet. Caller liefert eine Baseline-Lookup-Funktion die für
	/// einen gegebenen Tick die Baseline-Spielerliste returnt (oder null wenn nicht in History). Wenn
	/// <c>baselineTick == <see cref="NoBaselineTick"/></c> ist es ein Full-Snapshot — Baseline wird
	/// nicht abgefragt. Wenn die Baseline gebraucht aber nicht da ist, returnt die Funktion <c>false</c>
	/// (= packet droppen + nicht ack'en).</summary>
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
				return false; // Baseline rausgealtert — packet droppen. Client behält LastReceivedSnapshotTick = der
				              // alte Wert, Server schickt entweder noch ein delta gegen einen anderen baseline-Tick
				              // (wenn der existiert) oder altert irgendwann zu Full → self-healing.
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

	/// <summary>Writes a Hit packet directed at shooter + victim. Group ist byte (HitboxGroup-Enum),
	/// vorher String. Bytes statt String spart ~12 byte pro Hit-Event und gibt der UI direkten
	/// Enum-Zugriff (kein string-compare).</summary>
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

	/// <summary>Writes a Death packet — victim + attacker + weaponId + headshot-flag. Killfeed UI nutzt
	/// das alles für die Zeile ("Player X (M4A1) → Player Y [HS]"). weaponId = 0 für unbekannt/world-damage.</summary>
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

	// === ConVarSync (BIDIREKTIONAL Reliable) ===
	// Client → Server (Request): "set sv_debug_hitboxes 1". Server validiert (whitelist sv_*),
	// applied auf <see cref="ConVars.Sv"/>, broadcastet dann an ALLE Clients.
	// Server → Client (Broadcast): "sv_debug_hitboxes 1" — Client applied lokal damit Visualisierung-
	// Gates auf ConVars.Sv.* synchron bleiben.

	public static NetDataWriter WriteConVarSyncRequest(string name, string value)
	{
		var w = Begin(PacketType.ConVarSyncRequest);
		w.Put(name ?? "");
		w.Put(value ?? "");
		return w;
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

	// === DebugHitboxes (S2C Unreliable, ~10Hz, gated auf Dbg.Enabled am Server) ===
	// Pro Agent: netId + hitboxCount + Liste cm-quantized Hitbox-Positionen. Format ist absichtlich
	// klein gehalten (~92 byte pro Agent) damit es bei 16 Spielern ~15 KB/s extra ist. Client rendert
	// rote Spheres an jeder Position via <see cref="HudServerHitboxesDebug"/>.

	/// <summary>ONE agent per packet — bei 15 Hitboxen × 42 byte = ~640 byte je Packet, unter LiteNetLib's
	/// 1023 byte Unreliable-MTU. Server-Loop schickt pro Agent ein Packet.</summary>
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
			// Vollständige Basis als 3 Vec3 (36 byte) — INKL Scale. Quaternion-only war ein Bug
			// weil die tps_character-Skeleton-Scale (0.01) verloren ging → Client renderte mit
			// scale=1 → Capsule-Radius 28 wurde 28 METER statt 28cm × 0.01 = 28cm.
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

	/// <summary>Writes a ServerLog packet — a single UTF-8 string the client should print to its own
	/// stdout. Used to mirror server-side diagnostic events into the client's log window.</summary>
	public static NetDataWriter WriteServerLog(string message)
	{
		var w = Begin(PacketType.ServerLog);
		w.Put(message ?? "");
		return w;
	}

	/// <summary>Reads a ServerLog packet's UTF-8 string payload (cap 512 chars to bound any rogue
	/// server's bandwidth abuse).</summary>
	public static void ReadServerLog(NetPacketReader r, out string message)
	{
		message = r.GetString(512);
	}
}

/// <summary>Per-Agent Snapshot der Server-Hitbox-Transforms (Pos + Rot) für das Debug-Visualizations-System.</summary>
public struct DebugHitboxAgent
{
	public byte NetId;
	public Transform3D[] Transforms;
}

/// <summary>
/// Wire identifier per packet type. Keep stable — when the wire format changes incompatibly,
/// bump <see cref="Packets.ProtocolVersion"/>.
/// </summary>
public enum PacketType : byte
{
	ConnectRequest = 10,
	RespawnRequest = 11,
	/// <summary>C2S Reliable: Client requests setting a sv_* ConVar via console. Server validates +
	/// applies + broadcasts ConVarSync to all clients.</summary>
	ConVarSyncRequest = 12,

	SpawnAck = 20,
	PlayerJoined = 21,
	PlayerLeft = 22,
	PlayerDisconnected = 23,
	PlayerReconnected = 24,
	ShotFired = 25,
	Reload = 26,
	GrenadeSpawn = 27,
	Footstep = 28,
	Hit = 29,
	Death = 30,
	Respawn = 31,
	SlotSwitch = 32,
	Jump = 33,
	Land = 34,
	Inspect = 35,
	DryFire = 36,
	SlideStart = 37,
	SlideEnd = 38,
	RoundState = 39,
	ProjectileDespawn = 40,
	/// <summary>S2C Reliable: Server broadcastet eine sv_* ConVar-Änderung an alle Clients (auch Initial-Sync
	/// nach SpawnAck damit Reconnects den aktuellen Debug-State direkt haben).</summary>
	ConVarSyncBroadcast = 41,

	Input = 50,

	Snapshot = 70,
	ProjectileState = 71,
	/// <summary>Debug-only: Server-Hitbox-Positions broadcast (~10Hz, nur wenn Dbg.Enabled auf Server).
	/// Client rendert die als rote Spheres für visuelle Verifikation der Lag-Comp.</summary>
	DebugHitboxes = 72,
	/// <summary>S2C Reliable: server-side diagnostic/status string the client prints in its own log.
	/// Used to surface server events (FoW build progress, etc.) in the client's stdout when the
	/// server runs in a separate process whose stdout the user is not currently watching.</summary>
	ServerLog = 73,
}

/// <summary>Per-player block in the SnapshotPacket — server-authoritative state for one tick.</summary>
public struct SnapshotPlayer
{
	public byte NetId;
	public byte Flags;
	public Vector3 Pos;
	public Vector3 Vel;
	public float Yaw;
	public float Pitch;
	public byte AdsBlend;
	public byte CrouchBlend;
	public byte RaiseBlend;
	public ushort ShotIndex;
	public byte Hp;
	/// <summary>Kevlar 0..50. Wird ohne Regen verbraucht; Headshots bypassen.</summary>
	public byte Armor;
	public byte ActiveSlot;
	public byte WeaponId;
	public sbyte AimPunchX;
	public sbyte AimPunchY;
	public ushort FootstepPhase;
	public byte Kills;
	public byte Deaths;
	public byte PingMs;
	/// <summary>Server-broadcasted team for this player (cast of <see cref="Team"/>). Used by client for
	/// puppet team-glow + scoreboard color. None=0/CT=1/T=2/Deathmatch=3.</summary>
	public byte Team;
	/// <summary>Persistent index within the player's team (0..15), assigned at register-time, stable
	/// over the session. Drives the per-player color (palette[teamSlot]). Unique within a team —
	/// opposing teams may reuse the same slots/colors independently.</summary>
	public byte TeamSlot;
}

/// <summary>Bit flags packed into <see cref="SnapshotPlayer.Flags"/>.</summary>
[System.Flags]
public enum SnapshotFlags : byte
{
	None           = 0,
	Sliding        = 1 << 0,
	Airborne       = 1 << 1,
	Reloading      = 1 << 2,
	Sprinting      = 1 << 3,
	WallClinging   = 1 << 4,
	Inspecting     = 1 << 5,
	Dead           = 1 << 7,
}

/// <summary>Per-Player Field-Mask für Delta-Baseline-Snapshot-Compression. Pro Bit wird ein
/// Feld(-Gruppe) entweder geschickt (Bit = 1) oder weggelassen (= Wert bleibt = baseline-Wert).
///
/// Gruppierung folgt der "ändert sich typisch zusammen"-Heuristik: Pos+Vel beim Movement, Yaw+Pitch
/// beim Aimen, AdsBlend/CrouchBlend/RaiseBlend bei Posture-Wechseln, etc. Weniger Bits = kleinere
/// Mask, aber weniger Fine-Grain-Skip. 13 Bits passen in ushort.
///
/// <see cref="All"/> = alle Bits gesetzt → emittiert wie ein Full-Snapshot, z.B. wenn der Player
/// nicht in der Baseline war (frisch joined / kam zurück in PVS).</summary>
[System.Flags]
public enum SnapshotFieldFlags : ushort
{
	None      = 0,
	Flags     = 1 << 0,
	Movement  = 1 << 1,  // Pos + Vel
	View      = 1 << 2,  // Yaw + Pitch
	Blends    = 1 << 3,  // AdsBlend + CrouchBlend + RaiseBlend
	ShotIndex = 1 << 4,
	Hp        = 1 << 5,
	Armor     = 1 << 6,
	Weapon    = 1 << 7,  // ActiveSlot + WeaponId
	AimPunch  = 1 << 8,  // AimPunchX + AimPunchY
	Footstep  = 1 << 9,
	Score     = 1 << 10, // Kills + Deaths
	Ping      = 1 << 11,
	Team      = 1 << 12, // Team + TeamSlot
	All       = (1 << 13) - 1,
}

/// <summary>Parsed contents of one input frame within a <see cref="PacketType.Input"/> packet. Der
/// Packet-Header (count + ackedSnapshotTick) wird vorher mit <see cref="Packets.ReadInputHeader"/>
/// gelesen — daher steckt der Ack hier nicht mehr drin.</summary>
public struct InputPacket
{
	public uint TickIndex;
	public float ViewYaw;
	public float ViewPitch;
	public float WishX;
	public float WishZ;
	public bool SprintHeld, ShiftHeld, CrouchHeld, CrouchPressed, AdsHeld, BreathHoldHeld, JumpPressed, FirePressed;
	public bool ReloadPressed, InspectPressed, SlotIsGrenade;
	/// <summary>Sub-tick offset of the fire-press edge (0..255 → 0..0.996 of a tick). Only meaningful
	/// when <see cref="FirePressed"/> is true and the press occurred within the sending client's tick.
	/// Server adds <c>FireSubTick / 256f</c> to the lag-comp rewind tick (= rewinds <em>less far</em>
	/// = closer to the actual moment of click).</summary>
	public byte FireSubTick;
}

/// <summary>Initial world state for a player — flows through SpawnAck + PlayerJoined.</summary>
public struct InitialPlayerState
{
	public byte NetId;
	public string PlayerName;
	public Vector3 Position;
	public float Yaw;
	public byte Hp;
	public byte ActiveSlot;
	public byte WeaponId;
	/// <summary>Cast of <see cref="Team"/>. Required by puppets so team-glow works on first frame
	/// without waiting for the first snapshot.</summary>
	public byte Team;
	/// <summary>See <see cref="SnapshotPlayer.TeamSlot"/>. Sent at join so the puppet shows the right
	/// color before the first snapshot arrives.</summary>
	public byte TeamSlot;
}

/// <summary>Disconnect reason for <see cref="PacketType.PlayerLeft"/>.</summary>
public enum LeaveReason : byte
{
	Quit = 0,
	Timeout = 1,
	Kicked = 2,
	ServerShutdown = 3,
}
