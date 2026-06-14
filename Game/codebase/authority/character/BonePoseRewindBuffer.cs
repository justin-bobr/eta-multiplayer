using Godot;

namespace Vantix.Character;

/// <summary>
/// Per-tick bone pose history per <see cref="NetworkPlayer"/>, storing hitbox-node GlobalTransforms.
/// Lag-comp rewinds animated bone positions too, so headshots land when server/client animation
/// differ by a frame or two. Buffers 32 ticks (~250ms at 128Hz).
/// </summary>
public class BonePoseRewindBuffer
{
	private struct Entry { public uint Tick; public Transform3D[] Transforms; }
	private const int BufferSize = 32;
	private readonly Entry[] _ring = new Entry[BufferSize];
	private int _writeIdx;
	private int _count;
	private int _hitboxCount;
	/// <summary>Reusable result buffer for <see cref="QueryFractional"/> to avoid per-shot allocation.
	/// Consume synchronously — do not cache the returned reference across frames or pass to async code.</summary>
	private Transform3D[] _fractionalResult;

	/// <summary>Initializes the per-slot Transform3D arrays. Call once after HitboxRig.Build().</summary>
	public void Init(int hitboxCount)
	{
		_hitboxCount = hitboxCount;
		for (int i = 0; i < BufferSize; i++)
			_ring[i].Transforms = new Transform3D[hitboxCount];
		_fractionalResult = new Transform3D[hitboxCount];
	}

	/// <summary>Snapshots all CollisionShape3D GlobalTransforms into the ring buffer at the current tick.
	/// Uses the shape transform (not hitbox), since auto-orient offsets the shape from the hitbox origin.</summary>
	public void Push(uint tick, System.Collections.Generic.IReadOnlyList<CollisionShape3D> shapes)
	{
		if (_hitboxCount == 0 || shapes.Count != _hitboxCount) return;
		var entry = _ring[_writeIdx];
		entry.Tick = tick;
		for (int i = 0; i < _hitboxCount; i++)
			entry.Transforms[i] = shapes[i] != null ? shapes[i].GlobalTransform : Transform3D.Identity;
		_ring[_writeIdx] = entry;
		_writeIdx = (_writeIdx + 1) % BufferSize;
		if (_count < BufferSize) _count++;
	}

	/// <summary>Returns the Transform3D[] snapshot from the tick closest to and not newer than
	/// <paramref name="tick"/>. Returns null when no history is available (freshly spawned).</summary>
	public Transform3D[] Query(uint tick)
	{
		if (_count == 0) return null;
		for (int i = 0; i < _count; i++)
		{
			int idx = (_writeIdx - 1 - i + BufferSize) % BufferSize;
			ref var e = ref _ring[idx];
			if (e.Tick <= tick) return e.Transforms;
		}
		int oldestIdx = _count < BufferSize ? 0 : _writeIdx;
		return _ring[oldestIdx].Transforms;
	}

	/// <summary>Interpolates each bone transform between the two stored ticks bracketing
	/// <paramref name="fractionalTick"/> via <see cref="Transform3D.InterpolateWith"/>; clamps to the
	/// nearest endpoint when out of range. Returns null only when the buffer is empty. Returns the shared
	/// <see cref="_fractionalResult"/> buffer — consume synchronously, never cache or pass to async code.</summary>
	public Transform3D[] QueryFractional(float fractionalTick)
	{
		if (_count == 0) return null;
		if (fractionalTick <= 0f) return Query(0u);

		int newestIdx = (_writeIdx - 1 + BufferSize) % BufferSize;
		int oldestIdx = _count < BufferSize ? 0 : _writeIdx;

		if (fractionalTick >= (float)_ring[newestIdx].Tick) return _ring[newestIdx].Transforms;
		if (fractionalTick <= (float)_ring[oldestIdx].Tick) return _ring[oldestIdx].Transforms;

		int hiIdx = newestIdx;
		int loIdx = newestIdx;
		for (int i = 1; i < _count; i++)
		{
			hiIdx = loIdx;
			loIdx = (_writeIdx - 1 - i + BufferSize) % BufferSize;
			if ((float)_ring[loIdx].Tick <= fractionalTick && fractionalTick <= (float)_ring[hiIdx].Tick)
				break;
		}

		ref var a = ref _ring[loIdx];
		ref var b = ref _ring[hiIdx];
		float span = b.Tick - a.Tick;
		float f = span < 1e-5f ? 0f : Mathf.Clamp((fractionalTick - a.Tick) / span, 0f, 1f);
		for (int i = 0; i < _hitboxCount; i++)
			_fractionalResult[i] = a.Transforms[i].InterpolateWith(b.Transforms[i], f);
		return _fractionalResult;
	}
}
