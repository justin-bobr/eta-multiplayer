# ViewmodelEnvSync

Copies the level WorldEnvironment's look (tonemap, colour adjustment + LUT, glow, ambient tint/energy) onto the weapon viewmodel's own_world_3d Environment so the gun matches the loaded map. Keeps the viewmodel-specific setup (Sky ambient/reflection from `WorldCaptureRig`, SSAO, the `ViewmodelLightSampler` light rig). Called once from NetMain after the local player spawns.

## Methods

| Name | Summary |
|------|---------|
| `FindViewmodelEnv(Node)` | Returns the Environment of the own_world_3d SubViewport under the local player (= the weapon viewmodel env). |
| `FindWorldEnv(SceneTree)` | Returns the level's Environment (compositor-bearing WorldEnvironment, not a viewmodel own-world one), else the first non-viewmodel env. |
| `Sync(Node, SceneTree)` | Syncs viewmodel_env's look from the level WorldEnvironment. No-op if either env is missing. |
