# GlowViewportSync

Drives the local player's team-glow SubViewport pipeline. Each frame it keeps the body/text glow cameras locked to the FPS camera (transform + FOV/near/far) and matches the SubViewport size to the main viewport for 1:1 sampling, and toggles the glow CanvasLayer from Settings.TeamGlow. On _Ready it rebinds the composite body_tex/text_tex from live ViewportTextures (`RebindCompositeTextures`) and clones the world Environment for the glow cameras (`BuildGlowEnvironment`).

## Fields

| Name | Summary |
|------|---------|
| `MethodName.BuildGlowEnvironment` | Cached name for the 'BuildGlowEnvironment' method. |
| `MethodName.FindActiveWorldEnvironment` | Cached name for the 'FindActiveWorldEnvironment' method. |
| `MethodName.RebindCompositeTextures` | Cached name for the 'RebindCompositeTextures' method. |
| `MethodName.WalkForWorldEnvironment` | Cached name for the 'WalkForWorldEnvironment' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.BodyCamera` | Cached name for the 'BodyCamera' field. |
| `PropertyName.BodyViewport` | Cached name for the 'BodyViewport' field. |
| `PropertyName.CompositeRect` | Cached name for the 'CompositeRect' field. |
| `PropertyName.MainCamera` | Cached name for the 'MainCamera' field. |
| `PropertyName.RenderScale` | Cached name for the 'RenderScale' field. |
| `PropertyName.TextCamera` | Cached name for the 'TextCamera' field. |
| `PropertyName.TextViewport` | Cached name for the 'TextViewport' field. |
| `PropertyName._lastAppliedEnabled` | Cached name for the '_lastAppliedEnabled' field. |
| `PropertyName._lastSyncedSize` | Cached name for the '_lastSyncedSize' field. |
| `RenderScale` | Glow SubViewport render scale relative to the main viewport (1.0 = pixel-perfect). |

## Methods

| Name | Summary |
|------|---------|
| `BuildGlowEnvironment()` | Builds the glow-camera Environment by duplicating the active world Environment (so all grading/atmospherics match even if it leaks), then forcing background = transparent Color and disabling glow/adjustment. Glow is disabled so bright team_color channels aren't bloomed back into body_tex and smear the silhouette. |
| `FindActiveWorldEnvironment()` | Returns the first WorldEnvironment's Environment found from the tree root, or null. |
| `RebindCompositeTextures()` | Binds the composite material's body_tex/text_tex to the live SubViewport textures, sidestepping the .tscn ViewportTexture path-resolution failure in subscenes. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
