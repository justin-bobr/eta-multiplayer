# ViewmodelMotionBlur

Attaches a dedicated Compositor + `PostProcessEffect` to the weapon viewmodel's own WorldEnvironment, since the weapon's own_world_3d SubViewport is invisible to the world Compositor and would otherwise get no post-processing. `Configure` mirrors the world effect's toggles and uses the same compositor-path gating so it never double-applies with the FSR2 `PostCanvasFx` path. `Attach` wires the Compositor once after player spawn; `Configure` runs from Settings.ApplyEffects.

## Properties

| Name | Summary |
|------|---------|
| `Effect` | The per-viewmodel PostProcessEffect (null before `Attach`); exposed so the ADS feed can push AdsBlend onto the weapon pass. |

## Fields

| Name | Summary |
|------|---------|
| `WeaponBlurStrength` | Weapon motion blur is deliberately weaker than the world's; high muzzle angular velocity would otherwise smear the gun front. |

## Methods

| Name | Summary |
|------|---------|
| `Attach(Node)` | Attaches a Compositor + PostProcessEffect to the viewmodel WorldEnvironment. Calling twice replaces the previous attachment; `Configure` sets real toggles after. |
| `Configure(bool, bool, bool, bool, bool, bool)` | Mirrors the world effect's toggles onto the viewmodel effect (no-op if not attached). `enabled` must follow the world effect's compositor-path gating so it never stacks on the FSR2 PostCanvasFx pass. |
| `FindViewmodelEnvironment(Node)` | Returns the WorldEnvironment child of the LocalPlayer's own_world_3d SubViewport (the viewmodel viewport). |
| `IsViewmodelEnvironment(Node)` | True if the node lives inside an own_world_3d SubViewport (the viewmodel world); lets world-env finders skip the viewmodel's own compositor-bearing Environment. |
| `Reset()` | Clears the stored reference so the next Attach starts fresh after a level/player reload. |
| `SetEnabled(bool)` | Toggles the effect on/off. Safe to call before `Attach` — no-op if not yet attached. |
