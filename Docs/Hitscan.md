# Hitscan

Pure hitscan logic. Server-replayable: identical input + world-state yields identical HitInfo. Client runs it for visual impacts; server for damage authority and lag compensation.

## Fields

| Name | Summary |
|------|---------|
| `MaterialGroups` | Recognized surface material groups. Names match the audio/footsteps/ folders (1:1) plus "flesh". To add a ground type, add it here and create an identically named folder. |

## Methods

| Name | Summary |
|------|---------|
| `Cast(PhysicsDirectSpaceState3D, Vector3, Vector3, float, Nullable<Rid>, uint)` | Casts a ray for the given range (m). Optional single-RID exclude (e.g. the shooter). |
| `CastCore(PhysicsDirectSpaceState3D, PhysicsRayQueryParameters3D, Vector3, Vector3)` | Shared body for `Cast`/`CastMulti`: runs the IntersectRay and copies the result into a `HitInfo`, including per-face and per-group material lookup. |
| `CastMulti(PhysicsDirectSpaceState3D, Vector3, Vector3, float, Array<Rid>, uint)` | Like `Cast` but with a multi-RID exclude, so the shooter doesn't hit their own hitboxes (NetworkPlayer RID + all `Rids`). |
| `CastVsBoneShapes(Vector3, Vector3, List<ValueTuple<Node3D,Transform3D,Shape3D>>, float)` | Manual ray-vs-shape cast against (hitbox, world-transform, shape) tuples, using the rewound GlobalTransforms from the bone history buffer. Bypasses the physics broadphase, whose deferred-updated positions are stale for lag-comp. Returns the closest hit nearer than `maxDist`. |
| `DetectMaterialPerFace(Node3D, int)` | Per-face material lookup: maps face_index to a surface, reads its material's "impact_tag" metadata. Works only for trimesh colliders. Triangle counts are memoized per ArrayMesh (`_triCountCache`) since SurfaceGetArrays allocates per call. |
| `DetectMaterialPerGroup(Node3D)` | Per-collider group fallback - the first recognized material group wins. Cached per collider since group membership is static for the lifetime of the node. |
| `FindVisualMesh(Node3D)` | Looks for a MeshInstance3D as a sibling or child of the collider. Best-effort. Result is cached per collider to avoid repeated GetParent+GetChildren allocations. |
| `GetTriCounts(ArrayMesh)` | Returns the cached triangle counts per surface for the given mesh, populating the cache on first use. |
| `RayBox(Vector3, Vector3, Transform3D, Vector3, float)` | Ray-OBB (oriented bounding box). Slab test in local space (ray transformed into box-local frame). |
| `RayCapsule(Vector3, Vector3, Transform3D, float, float, float)` | Ray-capsule test. Capsule runs along local Y; evaluates cylinder + two end-spheres and returns the nearest hit. |
| `TriangleCount(Array)` | Returns the triangle count of a surface-array (indexed if present, otherwise vertex-based). |
