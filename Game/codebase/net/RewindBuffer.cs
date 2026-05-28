using Godot;

/// <summary>
/// Per-agent history ring of the server-authoritative position per tick. The <see cref="NetServer"/>
/// fills it every tick; server-side <see cref="Hitscan"/> rewinds other agents to
/// <c>shooterTick - RTT/2 - interpDelay</c> before casting the ray, providing fair lag compensation
/// for high-latency shooters.
///
/// Capacity is 128 ticks at 128 Hz = 1 s of history. Fixed-size circular ring — overwrites the oldest
/// entry in O(1) once <see cref="Capacity"/> is reached (no <c>List.RemoveAt(0)</c> shift). Lookups
/// use binary search since ticks are monotonically increasing.
/// </summary>
public class RewindBuffer
{
	/// <summary>One tick of recorded position history.</summary>
	public struct Entry { public uint Tick; public Vector3 Pos; }

	private const int Capacity = 128;
	private readonly Entry[] _buf = new Entry[Capacity];
	private int _head;
	private int _count;

	public int Count => _count;
	public uint NewestTick => _count > 0 ? _buf[(_head - 1 + Capacity) % Capacity].Tick : 0u;

	private int LogicalToArray(int logical) => (_head - _count + logical + Capacity) % Capacity;

	/// <summary>Appends a tick/position pair, discarding non-monotonic ticks and overwriting the oldest
	/// when at capacity.</summary>
	public void Push(uint tick, Vector3 pos)
	{
		if (_count > 0)
		{
			int last = (_head - 1 + Capacity) % Capacity;
			if (tick <= _buf[last].Tick) return;
		}
		_buf[_head].Tick = tick;
		_buf[_head].Pos = pos;
		_head = (_head + 1) % Capacity;
		if (_count < Capacity) _count++;
	}

	/// <summary>Returns the interpolated position for the given tick, clamping to the nearest endpoint
	/// when out of range. Binary-searches for the bracket — O(log n) per query.</summary>
	public Vector3 Query(uint tick)
	{
		if (_count == 0) return Vector3.Zero;
		int oldestArr = LogicalToArray(0);
		int newestArr = (_head - 1 + Capacity) % Capacity;
		if (tick <= _buf[oldestArr].Tick) return _buf[oldestArr].Pos;
		if (tick >= _buf[newestArr].Tick) return _buf[newestArr].Pos;

		int lo = 0, hi = _count - 1;
		while (hi - lo > 1)
		{
			int mid = (lo + hi) >> 1;
			uint midTick = _buf[LogicalToArray(mid)].Tick;
			if (midTick == tick) return _buf[LogicalToArray(mid)].Pos;
			if (midTick < tick) lo = mid; else hi = mid;
		}
		var a = _buf[LogicalToArray(lo)];
		var b = _buf[LogicalToArray(hi)];
		uint span = b.Tick - a.Tick;
		float t = span == 0u ? 0f : (tick - a.Tick) / (float)span;
		return a.Pos.Lerp(b.Pos, t);
	}

	/// <summary>Clears all recorded entries.</summary>
	public void Clear() { _count = 0; _head = 0; }
}
