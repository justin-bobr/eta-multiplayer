# BulletTracerPool

MultiMesh bullet tracer pool: all tracers render in one draw call. Fixed-size ring buffer with swap-and-pop expiry, per-instance Transform3D + Color (alpha fade via VertexColorUseAsAlbedo). Mirrors `ShellPool`; LocalAnimation.TriggerBulletTracer calls `Instance?.Emit(...)`.

## Fields

| Name | Summary |
|------|---------|
| `Instance` | Singleton; LocalAnimation references via `BulletTracerPool.Instance?.Emit(...)`. |
| `MethodName.Emit` | Cached name for the 'Emit' method. |
| `MethodName.WriteInstance` | Cached name for the 'WriteInstance' method. |
| `MethodName._ExitTree` | Cached name for the '_ExitTree' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.DefaultColor` | Cached name for the 'DefaultColor' field. |
| `PropertyName.MaxTracers` | Cached name for the 'MaxTracers' field. |
| `PropertyName.TracerRadius` | Cached name for the 'TracerRadius' field. |
| `PropertyName._activeCount` | Cached name for the '_activeCount' field. |
| `PropertyName._mm` | Cached name for the '_mm' field. |
| `PropertyName._mmi` | Cached name for the '_mmi' field. |
| `PropertyName._overflowCursor` | Cached name for the '_overflowCursor' field. |

## Methods

| Name | Summary |
|------|---------|
| `Emit(Vector3, Vector3, Color, float, float)` | Spawns a tracer from origin toward endpoint. Color alpha fades over total flight + streak time. Overflow recycles the oldest slot. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `WriteInstance(int)` | Writes a tracer's transform + color into the MultiMesh. Cylinder Y-axis aligned to direction, scale.Y = streakLength, position = midpoint of streak (= front - dir × halfStreak). |
| `_ExitTree()` | Clears the singleton when the pool leaves the tree. |
| `_Process(double)` | Advances every active tracer: moves the streak forward, fades alpha, expires when front passes endpoint. |
| `_Ready()` | Initialises the MultiMesh with a thin cylinder mesh and registers the singleton. |
