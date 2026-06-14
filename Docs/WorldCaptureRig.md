# WorldCaptureRig

Live position-aware reflection cubemap for the own_world_3d weapon viewmodel, which can't see the main world's probes/GI/Sky. Hosts 6 SubViewport+Camera3D pairs at the player position facing each cube direction (world cull mask only, so the gun doesn't self-reflect), refreshes one face per FaceUpdateInterval frames round-robin, and feeds each ViewportTexture to `viewmodel_cube_sky.gdshader`. Face order MUST match the shader's face_* uniforms: px, nx, py, ny, pz, nz. Cameras are repositioned each frame but stay axis-aligned (player turning must not rotate the cube). The Sky process_mode must be REALTIME.

## Fields

| Name | Summary |
|------|---------|
| `AnchorCamera` | Camera the cube is positioned at each frame (typically fps_camera). Anchor rotation is not applied. |
| `CaptureCullMask` | Cube-camera cull mask; defaults to world layer 1, excluding the viewmodel so the gun doesn't self-reflect. |
| `CaptureFar` | Cube-camera far plane, kept short: it feeds a 64px IBL, and a large far re-renders the whole map (incl. directional shadows) per face. |
| `FaceUpdateInterval` | Frames between single-face updates (higher = cheaper). Throttled and gated behind Settings.Reflections; flat-out it's a measured 300->30 FPS hit. |
| `Faces` | The 6 cube-face SubViewports, order +X, -X, +Y, -Y, +Z, -Z, each with one Camera3D child. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.AnchorCamera` | Cached name for the 'AnchorCamera' field. |
| `PropertyName.CaptureCullMask` | Cached name for the 'CaptureCullMask' field. |
| `PropertyName.CaptureFar` | Cached name for the 'CaptureFar' field. |
| `PropertyName.FaceUpdateInterval` | Cached name for the 'FaceUpdateInterval' field. |
| `PropertyName.Faces` | Cached name for the 'Faces' field. |
| `PropertyName.ViewmodelSky` | Cached name for the 'ViewmodelSky' field. |
| `PropertyName._currentFace` | Cached name for the '_currentFace' field. |
| `PropertyName._faceCams` | Cached name for the '_faceCams' field. |
| `PropertyName._frameAccum` | Cached name for the '_frameAccum' field. |
| `ViewmodelSky` | Sky whose sky_material (viewmodel_cube_sky.gdshader) receives the 6 ViewportTextures at _Ready. |

## Methods

| Name | Summary |
|------|---------|
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
