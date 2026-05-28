/// <summary>
/// Ring-Buffer für Snapshot-Baselines (Delta-Baseline-Compression).
///
/// Server-Seite: pro Peer ein Ring mit den letzten <see cref="Capacity"/> Snapshots die AN diesen
/// Peer geschickt wurden (post-PVS-filter). Bei jedem Snapshot-Send sucht der Server den Eintrag
/// passend zur <see cref="PeerState.LastAckedSnapshotTick"/> des Peers und sendet die Delta dazu.
///
/// Client-Seite: ein Ring mit den letzten empfangenen + voll rekonstruierten Snapshots. Wenn ein
/// neues Snapshot-Packet mit <c>baselineTick != 0</c> ankommt, lookup'n wir die Baseline hier und
/// applien die Delta-Felder darauf um den vollen State zu rekonstruieren.
///
/// Capacity = 64 deckt ~1 sec bei 64Hz Snapshot-Rate — toleriert ~500ms RTT bevor die Baseline
/// rausfällt + der nächste Snapshot zu einem Full degeneriert (was bandbreitemäßig OK ist und
/// von alleine wieder einrastet).
/// </summary>
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

	/// <summary>Speichert den Snapshot im aktuellen Ring-Slot. Das interne SnapshotPlayer[] wird nur
	/// dann grown wenn die Spielerzahl steigt, sonst überschrieben (zero-alloc steady-state).</summary>
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

	/// <summary>Variant für SnapshotPlayer[]-Buffer (Client-Side hält die Snapshots schon als Array).</summary>
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

	/// <summary>Returnt den Eintrag für den gegebenen Tick oder null wenn nicht in History
	/// (rausgealtert oder nie geschickt/empfangen). Linear-Scan über 64 Slots ist trivial.</summary>
	public Entry Find(uint tick)
	{
		for (int i = 0; i < Capacity; i++)
		{
			var e = _ring[i];
			if (e != null && e.Valid && e.Tick == tick) return e;
		}
		return null;
	}

	/// <summary>Invalidiert alle Einträge. Aufrufen bei Session-Reset (Reconnect, neue Map).</summary>
	public void Clear()
	{
		for (int i = 0; i < Capacity; i++)
			if (_ring[i] != null) _ring[i].Valid = false;
		_pushCount = 0;
	}
}
