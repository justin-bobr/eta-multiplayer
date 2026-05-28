using Godot;

/// <summary>
/// Ring buffer of local prediction states. One entry is pushed per <see cref="PlayerCore.FixedTick"/>
/// containing the post-step <see cref="MovementSnapshot"/> and position. When the server sends an
/// authoritative snapshot with <c>ackedInputTick = N</c>, the stored position at tick N is compared
/// against the server position at tick N to compute the mispredict drift.
///
/// Fixed-size circular ring (<see cref="Capacity"/> = 256 ticks ≈ 2 s @ 128 Hz). Entries past capacity
/// overwrite the oldest in O(1) — no <c>List.RemoveAt(0)</c> shift like the old impl. Lookups use
/// binary search since ticks are monotonically increasing.
/// </summary>
public class PredictionBuffer
{
	/// <summary>One predicted tick entry: input, post-step snapshot, and resulting position/velocity.</summary>
	public struct Entry
	{
		public uint Tick;
		public MovementInput Input;
		public MovementSnapshot State;
		public Vector3 PostPos;
		public Vector3 PostVel;
	}

	private const int Capacity = 256;
	private readonly Entry[] _buf = new Entry[Capacity];
	private int _head;
	private int _count;

	public int Count => _count;

	/// <summary>Maps a logical index (0 = oldest, Count-1 = newest) to the underlying array index
	/// accounting for ring wraparound.</summary>
	private int LogicalToArray(int logical) => (_head - _count + logical + Capacity) % Capacity;

	/// <summary>Appends a new tick entry. Discards non-monotonic ticks; overwrites the oldest entry
	/// once <see cref="Capacity"/> is reached.</summary>
	public void Push(uint tick, in MovementInput input, in MovementSnapshot state, Vector3 postPos, Vector3 postVel)
	{
		if (_count > 0)
		{
			int last = (_head - 1 + Capacity) % Capacity;
			if (tick <= _buf[last].Tick) return;
		}
		_buf[_head].Tick = tick;
		_buf[_head].Input = input;
		_buf[_head].State = state;
		_buf[_head].PostPos = postPos;
		_buf[_head].PostVel = postVel;
		_head = (_head + 1) % Capacity;
		if (_count < Capacity) _count++;
	}

	/// <summary>Binary-search by tick (entries are monotonic). On hit returns logical index;
	/// on miss returns lower-bound logical index (= first index whose tick is &gt; <paramref name="tick"/>).</summary>
	private bool TryFindIndex(uint tick, out int logicalIndex)
	{
		int lo = 0, hi = _count - 1;
		while (lo <= hi)
		{
			int mid = (lo + hi) >> 1;
			uint midTick = _buf[LogicalToArray(mid)].Tick;
			if (midTick == tick) { logicalIndex = mid; return true; }
			if (midTick < tick) lo = mid + 1;
			else hi = mid - 1;
		}
		logicalIndex = lo;
		return false;
	}

	/// <summary>Looks up the entry for the exact tick; returns false if it has rolled out of the buffer.</summary>
	public bool TryGet(uint tick, out Entry entry)
	{
		if (TryFindIndex(tick, out int idx))
		{
			entry = _buf[LogicalToArray(idx)];
			return true;
		}
		entry = default;
		return false;
	}

	/// <summary>Returns the logical index of the first entry whose tick is &gt; <paramref name="afterTick"/>,
	/// or <see cref="Count"/> if none. Used by reconcile replay loops.</summary>
	public int FindFirstIndexAfter(uint afterTick)
	{
		if (_count == 0) return 0;
		int lo = 0, hi = _count - 1;
		while (lo <= hi)
		{
			int mid = (lo + hi) >> 1;
			if (_buf[LogicalToArray(mid)].Tick <= afterTick) lo = mid + 1;
			else hi = mid - 1;
		}
		return lo;
	}

	/// <summary>Random access by logical index — caller must check bounds via <see cref="Count"/>.</summary>
	public Entry GetAt(int logicalIndex) => _buf[LogicalToArray(logicalIndex)];

	/// <summary>Updates the cached state of an existing entry after a replay step so later reconciliations
	/// see the new value.</summary>
	public void UpdateEntryState(uint tick, in MovementSnapshot newState, Vector3 newPos, Vector3 newVel)
	{
		if (!TryFindIndex(tick, out int idx)) return;
		int arr = LogicalToArray(idx);
		_buf[arr].State = newState;
		_buf[arr].PostPos = newPos;
		_buf[arr].PostVel = newVel;
	}

	/// <summary>Clears all entries from the buffer.</summary>
	public void Clear() { _count = 0; _head = 0; }
}
