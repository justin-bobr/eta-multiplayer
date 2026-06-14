# GlowSilhouetteMeshBaker

Editor tool + runtime asset. As a child of the puppet's Skeleton3D, the inspector `Bake` trigger merges every visible skinned body MeshInstance3D's silhouette into this node's own `Mesh`; at runtime it's a regular skinned MeshInstance3D that PuppetPlayer toggles and recolours via SetInstanceShaderParameter("team_color", …). The bake welds vertices (`WeldVerts`) and suppresses boundary spikes (`SuppressBoundarySpikes`); `Cancel` aborts in progress, leaving Mesh unchanged. Hidden meshes are skipped.

## Properties

| Name | Summary |
|------|---------|
| `GlowColor` | Default team colour for the glow chain; the setter updates the live material chain. PuppetPlayer overrides it per-instance at runtime. |

## Fields

| Name | Summary |
|------|---------|
| `CustomOutlineMaterial` | Optional material override; null builds the default outline/fade ShaderMaterial chain. |
| `ExcludedMeshes` | MeshInstance3D NodePaths to exclude from the bake (resolved fresh per bake). For variants that must stay enabled for gameplay but should not be in the silhouette. Unresolvable paths are logged and skipped. |
| `GlowMaxWidth` | World-metre extent of the fade tail past the second band, across `GlowShellCount` shells. |
| `GlowShellCount` | Number of fade-tail shells (more = smoother gradient, one draw call each). |
| `GlowStartAlpha` | Starting alpha of the inner rim; the second band and fade shells scale from it. Master glow-intensity knob. |
| `MethodName.AbortBake` | Cached name for the 'AbortBake' method. |
| `MethodName.AppendMeshSurfaces` | Cached name for the 'AppendMeshSurfaces' method. |
| `MethodName.BuildDefaultOutlineMaterialChain` | Cached name for the 'BuildDefaultOutlineMaterialChain' method. |
| `MethodName.PropagateGlowColorToChain` | Cached name for the 'PropagateGlowColorToChain' method. |
| `MethodName.RemoveDuplicateTriangles` | Cached name for the 'RemoveDuplicateTriangles' method. |
| `MethodName.RequestCancel` | Cached name for the 'RequestCancel' method. |
| `MethodName.StartBake` | Cached name for the 'StartBake' method. |
| `MethodName.SuppressBoundarySpikes` | Cached name for the 'SuppressBoundarySpikes' method. |
| `MethodName.UpdateStatus` | Cached name for the 'UpdateStatus' method. |
| `MethodName.WeldAndCommit` | Cached name for the 'WeldAndCommit' method. |
| `MethodName.WeldVerts` | Cached name for the 'WeldVerts' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `OutlineWidth` | Inner sharp rim width in world metres, auto-scaled by skeleton scale at bake time. |
| `PropertyName.Bake` | Cached name for the 'Bake' property. |
| `PropertyName.Cancel` | Cached name for the 'Cancel' property. |
| `PropertyName.CustomOutlineMaterial` | Cached name for the 'CustomOutlineMaterial' field. |
| `PropertyName.ExcludedMeshes` | Cached name for the 'ExcludedMeshes' field. |
| `PropertyName.GlowColor` | Cached name for the 'GlowColor' property. |
| `PropertyName.GlowMaxWidth` | Cached name for the 'GlowMaxWidth' field. |
| `PropertyName.GlowShellCount` | Cached name for the 'GlowShellCount' field. |
| `PropertyName.GlowStartAlpha` | Cached name for the 'GlowStartAlpha' field. |
| `PropertyName.OutlineWidth` | Cached name for the 'OutlineWidth' field. |
| `PropertyName.SecondLayerWidth` | Cached name for the 'SecondLayerWidth' field. |
| `PropertyName.Status` | Cached name for the 'Status' property. |
| `PropertyName.WeldEpsilon` | Cached name for the 'WeldEpsilon' field. |
| `PropertyName._bakeMeshIdx` | Cached name for the '_bakeMeshIdx' field. |
| `PropertyName._bakeSkeleton` | Cached name for the '_bakeSkeleton' field. |
| `PropertyName._baking` | Cached name for the '_baking' field. |
| `PropertyName._boneCountPerVertex` | Cached name for the '_boneCountPerVertex' field. |
| `PropertyName._cancelRequested` | Cached name for the '_cancelRequested' field. |
| `PropertyName._glowColor` | Cached name for the '_glowColor' field. |
| `PropertyName._sourceSkin` | Cached name for the '_sourceSkin' field. |
| `PropertyName._status` | Cached name for the '_status' field. |
| `SecondLayerWidth` | Width of the second solid band (the main visible halo) just outside the inner rim. |

## Methods

| Name | Summary |
|------|---------|
| `BuildDefaultOutlineMaterialChain()` | Builds the default outline-hull + second-band + fade-shell + xray material chain (widths scale-corrected). PuppetPlayer overrides team_color per-instance. |
| `PropagateGlowColorToChain()` | Pushes `_glowColor` onto every shell's `team_color` uniform down the next_pass chain. |
| `RemoveDuplicateTriangles()` | Drops degenerate, sub-millimetre sliver, and duplicate triangles (same vertex indices after welding overlapping source meshes). |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SuppressBoundarySpikes()` | Replaces open-boundary vertex normals (edges in exactly one triangle) with the average of their non-boundary neighbours so the hull push doesn't spike; zeroes the normal when no neighbour smooths it. |
| `WalkForBodyMeshes(Node, List<MeshInstance3D>)` | Collects every visible MeshInstance3D under the parent Skeleton3D, skipping this baker node, hidden meshes, and `ExcludedMeshes`. |
| `WeldVerts()` | Spatial-hash vertex weld; merged normals are averaged so the hull push is smooth, closing cut edges between body meshes. `WeldEpsilon` is world-metres and is rescaled by the skeleton's basis scale into mesh-local units before comparing (mesh is typically cm-authored under a 0.01x transform). |
