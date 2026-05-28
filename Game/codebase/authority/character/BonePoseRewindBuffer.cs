using Godot;

/// <summary>
/// Bone-Pose-History pro <see cref="ServerBaseCharacter"/>. Speichert pro Tick die <see cref="Transform3D"/>
/// aller Hitbox-Nodes (GlobalTransform). Lag-Comp bei RunAuthoritativeHitscan rewindt damit nicht nur
/// die Body-Position sondern auch die animierten Bone-Positions — sonst trifft ein Schuss in den Kopf
/// daneben wenn die Server-Animation 1-2 Frames vor/hinter der Client-Animation läuft (gleicher
/// Effekt für leicht moving Bones wie Atem-Sway in IDLE-Animation).
///
/// CS2-Style "Subtick"-Approach mit reduziertem Scope: nur die Hitbox-Nodes (~15) statt aller Bones (~80).
/// Buffer 32 Ticks = 250ms bei 128Hz Server-Tick — deckt typische Round-Trip-Lag von 200ms Spielern.
/// </summary>
public class BonePoseRewindBuffer
{
	private struct Entry { public uint Tick; public Transform3D[] Transforms; }
	private const int BufferSize = 32;
	private readonly Entry[] _ring = new Entry[BufferSize];
	private int _writeIdx;
	private int _count;
	private int _hitboxCount;
	/// <summary>Reusable result buffer for <see cref="QueryFractional"/> so subtick-fire hitscan does
	/// not allocate per shot. Sized in <see cref="Init"/>. Safe to share across queries because
	/// <see cref="Transform3D"/> is a value type: every consumer (the hitscan loop in
	/// <c>PlayerCore.RunAuthoritativeHitscan</c>) reads the buffer slot into a local struct at use-site,
	/// so a subsequent <see cref="QueryFractional"/> call that overwrites the buffer cannot disturb a
	/// previously-issued snapshot. Callers must still consume synchronously — do NOT cache the returned
	/// reference across frames or hand it to async code.</summary>
	private Transform3D[] _fractionalResult;

	/// <summary>Initialisiert die per-Slot Transform3D-Arrays. Aufruf einmal nach HitboxRig.Build().</summary>
	public void Init(int hitboxCount)
	{
		_hitboxCount = hitboxCount;
		for (int i = 0; i < BufferSize; i++)
			_ring[i].Transforms = new Transform3D[hitboxCount];
		_fractionalResult = new Transform3D[hitboxCount];
	}

	/// <summary>Snapshot aller CollisionShape3D-GlobalTransforms in den Ring-Buffer am aktuellen
	/// Server-Tick. WICHTIG: Shape-Transform statt Hitbox-Transform, weil Auto-Orient die Shape um
	/// einen Offset gegenüber dem Hitbox-Origin verschiebt (Capsule sitzt mittig zwischen Bone und
	/// Child-Bone). Manueller Ray-vs-Shape-Cast braucht die echte Shape-World-Pose.</summary>
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

	/// <summary>Liefert die Transform3D[]-Snapshot vom Tick ≤ <paramref name="tick"/> (= nearest älter).
	/// Returns null wenn keine History vorhanden (frisch gespawned).</summary>
	public Transform3D[] Query(uint tick)
	{
		if (_count == 0) return null;
		// Walk backwards from newest, find first entry with Tick <= tick.
		for (int i = 0; i < _count; i++)
		{
			int idx = (_writeIdx - 1 - i + BufferSize) % BufferSize;
			ref var e = ref _ring[idx];
			if (e.Tick <= tick) return e.Transforms;
		}
		// Alle Einträge sind NEUER als tick → ältesten zurückgeben (best effort).
		int oldestIdx = _count < BufferSize ? 0 : _writeIdx;
		return _ring[oldestIdx].Transforms;
	}

	/// <summary>Subtick-fire variant: interpolates each bone transform between the two stored ticks
	/// bracketing <paramref name="fractionalTick"/>, using <see cref="Transform3D.InterpolateWith"/>
	/// (linear for origin, quaternion-slerp for basis). When the fractional tick lies outside the
	/// stored range, clamps to the nearest endpoint without interpolation — matching <see cref="Query"/>'s
	/// best-effort fallback. Returns null only when the buffer is completely empty.
	///
	/// The returned array is the internal reuse buffer <see cref="_fractionalResult"/> — callers must
	/// consume it synchronously (hitscan does), never cache or hand to async code.</summary>
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
