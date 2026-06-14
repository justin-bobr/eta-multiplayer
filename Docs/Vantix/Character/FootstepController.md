# FootstepController

`Vantix.Character.FootstepController`

Deterministic distance-based footstep cadence. Pure logic (no Node3D/Physics/Random), so it is server- and client-replay safe like `MovementController`. State is a continuous `ContinuousPhase` in step units that grows by 1.0 per traveled `FootstepStrideLength`; each integer crossing is a step. The same phase is the master clock for the view-bob in LocalAnimation, so bob and step sound stay in sync. The server runs this per player and broadcasts step events; remote clients play them spatially. `StepLoudness` (0..1) is the gameplay-relevant audibility.

## Properties

| Name | Summary |
|------|---------|
| `ContinuousPhase` | Continuous step phase wrapped to [0,2), master clock for the view-bob. +1.0 per step, integers mark footplants; a full L+R gait cycle is 2.0. The wrap keeps float precision bounded. |
| `DidStepThisFrame` | True in exactly the tick in which an audible step lands. |
| `StepIsLeftFoot` | Alternates per step (L/R) for deterministic client-side sample selection. |
| `StepLoudness` | 0..1 audibility of the step, speed-scaled. Shift causes the step to not be emitted at all. |
| `StridePhase` | 0..1 progress to the next step, e.g. for the debug overlay. |

## Fields

| Name | Summary |
|------|---------|
| `Sv` | Tuning reference. Default is the global `Sv`. Swappable for tests. |

## Methods

| Name | Summary |
|------|---------|
| `ComputeLoudness(Vantix.Character.FootstepInput)` | 0..1 audibility. Shift returns 0 (silent); otherwise speed-banded and dampened by crouch. |
| `Reset()` | Resets the cadence. Caller: respawn / teleport, to avoid a phantom step afterwards. |
| `Step(Vantix.Character.FootstepInput)` | Server-replayable footstep step. Advances the phase and emits step events. |
| `StrideLength(Vantix.Character.FootstepInput)` | Stride length (m) between two footsteps. Sprint shortens, crouch lengthens cadence. |
