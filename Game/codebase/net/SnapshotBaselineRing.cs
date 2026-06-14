/// <summary>Ring buffer of snapshot baselines for delta compression.
/// Server: one ring per peer of the last <see cref="Capacity"/> snapshots sent to that peer (post-PVS); each
/// send delta's against the entry matching <see cref="PeerState.LastAckedSnapshotTick"/>.
/// Client: ring of the last received + reconstructed snapshots; a packet with <c>baselineTick != 0</c> applies
/// its delta onto the looked-up baseline.
/// Capacity 64 (~1 s @ 64 Hz) tolerates ~500ms RTT before the baseline ages out and the next snapshot goes full (self-healing).</summary>
public class SnapshotBaselineRing
{
	private const int Capacity = 64;
	private readonly Entry[] _ring = new Entry[Capacity];
	private uint _pushCount;

	public class Entry
	{
		public uint Tick;
		public bool Valid;
		public SnapshotPlayer[] Players = System.Array.Empty<SnapshotPlayer>();
		public int PlayerCount;
	}

	/// <summary>Stores the snapshot in the current ring slot. The internal SnapshotPlayer[] is only
	/// grown when the player count increases; otherwise it is overwritten in place (zero-alloc steady state).</summary>
	public void Push(uint tick, System.Collections.Generic.IReadOnlyList<SnapshotPlayer> players)
	{
		int count = players.Count;
		int slot = (int)(_pushCount % Capacity);
		var e = _ring[slot] ??= new Entry();
		if (e.Players.Length < count) e.Players = new SnapshotPlayer[count];
		for (int i = 0; i < count; i++) e.Players[i] = players[i];
		e.PlayerCount = count;
		e.Tick = tick;
		e.Valid = true;
		_pushCount++;
	}

	/// <summary>Variant for a SnapshotPlayer[] buffer (client side already holds snapshots as an array).</summary>
	public void Push(uint tick, SnapshotPlayer[] players, int count)
	{
		int slot = (int)(_pushCount % Capacity);
		var e = _ring[slot] ??= new Entry();
		if (e.Players.Length < count) e.Players = new SnapshotPlayer[count];
		for (int i = 0; i < count; i++) e.Players[i] = players[i];
		e.PlayerCount = count;
		e.Tick = tick;
		e.Valid = true;
		_pushCount++;
	}

	/// <summary>Returns the entry for the given tick, or null if not in history
	/// (aged out or never sent/received). A linear scan over 64 slots is trivial.</summary>
	public Entry Find(uint tick)
	{
		for (int i = 0; i < Capacity; i++)
		{
			var e = _ring[i];
			if (e != null && e.Valid && e.Tick == tick) return e;
		}
		return null;
	}

	/// <summary>Invalidates all entries. Call on session reset (reconnect, new map).</summary>
	public void Clear()
	{
		for (int i = 0; i < Capacity; i++)
			if (_ring[i] != null) _ring[i].Valid = false;
		_pushCount = 0;
	}
}
