# BulletTracer

`Vantix.Fx.BulletTracer`

A fixed-length cylindrical streak that travels at bullet speed from muzzle to impact. Spawn with `Spawn`; auto-frees once its front passes the endpoint.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.Initialize` | Cached name for the 'Initialize' method. |
| `MethodName.Spawn` | Cached name for the 'Spawn' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `PropertyName._age` | Cached name for the '_age' field. |
| `PropertyName._direction` | Cached name for the '_direction' field. |
| `PropertyName._material` | Cached name for the '_material' field. |
| `PropertyName._mesh` | Cached name for the '_mesh' field. |
| `PropertyName._origin` | Cached name for the '_origin' field. |
| `PropertyName._speed` | Cached name for the '_speed' field. |
| `PropertyName._startColor` | Cached name for the '_startColor' field. |
| `PropertyName._streakLength` | Cached name for the '_streakLength' field. |
| `PropertyName._totalDistance` | Cached name for the '_totalDistance' field. |

## Methods

| Name | Summary |
|------|---------|
| `Initialize(Vector3, Vector3, Color, float, float, float)` | Builds the cylinder mesh/material and places the streak just behind the origin. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `Spawn(SceneTree, Vector3, Vector3, Color, float, float, float)` | Creates a tracer under the scene root, initializes it and returns the instance. |
| `_Process(double)` | Advances the streak each frame and frees the node once its front has passed the endpoint. |
