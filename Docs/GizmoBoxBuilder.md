# GizmoBoxBuilder

Shared box-wireframe helper for the gizmo plugins. Returns the 24 endpoints of the 12 edges of an AABB centred at origin with the given size — laid out as line pairs ready for `AddLines`. Kept centralised so Zone and BombSpot draw identical outlines.

## Methods

| Name | Summary |
|------|---------|
| `BuildBoxMesh(Vector3)` | Solid `BoxMesh` sized to the given extents, used as the transparent fill body for the gizmo. Pairs with `BuildLines` at the same size so the outline and fill match exactly. |
