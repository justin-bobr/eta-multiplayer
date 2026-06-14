# TpsFootIk

`Vantix.Character.TpsFootIk`

TPS Foot IK: TwoBoneIK3D per leg plus a ground-snap raycast. Ground adaptation only, no procedural stepping. Requires a TwoBoneIK3D, a foot-target Marker3D and a pole Marker3D per leg. Needs improvement.

## Methods

| Name | Summary |
|------|---------|
| `EnsureMarker(MeshInstance3D, Node, Color, float)` | Lazily creates an unshaded debug sphere marker as a child of the given parent. |
| `GroundSnap(Vector3, PhysicsDirectSpaceState3D)` | Raycasts down and snaps the world position to the hit, adding FootGroundOffset to compensate for the foot bone height above the mesh footprint. |
| `Initialize(Skeleton3D)` | Resolves the thigh bones and validates that the wired TwoBoneIK3D refs are present. Returns false (and disables IK) if anything is missing. |
| `SetIkProperty(Node, float)` | Pushes the active/influence properties to a TwoBoneIK3D node via dynamic Set. |
| `Update(float, Vector3, Vector3, Basis, PhysicsDirectSpaceState3D)` | Per-tick update: snaps both foot targets to the ground beneath the hips, positions the knee pole markers, and smooth-lerps the IK influence. |
| `UpdateDebugMarkers(Vector3, Vector3)` | Spawns/updates the debug sphere markers at the snapped foot positions. |
| `UpdateDebugRays()` | Rebuilds the immediate-mesh debug lines for the collected ground-probe rays this frame. |
