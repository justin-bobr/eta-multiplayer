# GizmoBoxBuilder

`Vantix.Utils.GizmoBoxBuilder`

Shared box-wireframe helper for the gizmo plugins. Returns the 24 endpoints of the 12 edges of an AABB centred at origin, laid out as line pairs for `AddLines`. Centralised so all gizmos draw identical outlines.

## Methods

| Name | Summary |
|------|---------|
| `BuildBoxMesh(Vector3)` | Solid `BoxMesh` for the gizmo's transparent fill body. Matches `BuildLines` at the same size. |
