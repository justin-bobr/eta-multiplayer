# BulletDecalSet

Decal texture bundle for one material type (.tres resource): Albedo, Normal, ORM, Emission. ORM is either pre-packed or auto-packed (and cached) on first access from separate O/R/M inputs.

## Fields

| Name | Summary |
|------|---------|
| `MaxPackResolution` | Resolution cap for the generated ORM/Albedo textures. |
| `MethodName.GetEffectiveAlbedo` | Cached name for the 'GetEffectiveAlbedo' method. |
| `MethodName.GetEffectiveOrm` | Cached name for the 'GetEffectiveOrm' method. |
| `MethodName.MergeAlbedoOpacity` | Cached name for the 'MergeAlbedoOpacity' method. |
| `MethodName.NormalizedBytes` | Cached name for the 'NormalizedBytes' method. |
| `MethodName.PackOrm` | Cached name for the 'PackOrm' method. |
| `Opacity` | Optional separate alpha mask, merged into Albedo.A when Albedo has no alpha. |
| `PropertyName.Albedo` | Cached name for the 'Albedo' field. |
| `PropertyName.AlbedoMix` | Cached name for the 'AlbedoMix' field. |
| `PropertyName.Emission` | Cached name for the 'Emission' field. |
| `PropertyName.MaxPackResolution` | Cached name for the 'MaxPackResolution' field. |
| `PropertyName.Metallic` | Cached name for the 'Metallic' field. |
| `PropertyName.Modulate` | Cached name for the 'Modulate' field. |
| `PropertyName.Normal` | Cached name for the 'Normal' field. |
| `PropertyName.NormalFade` | Cached name for the 'NormalFade' field. |
| `PropertyName.Occlusion` | Cached name for the 'Occlusion' field. |
| `PropertyName.Opacity` | Cached name for the 'Opacity' field. |
| `PropertyName.Roughness` | Cached name for the 'Roughness' field. |
| `PropertyName._albedoPackTried` | Cached name for the '_albedoPackTried' field. |
| `PropertyName._ormPackTried` | Cached name for the '_ormPackTried' field. |
| `PropertyName._packedAlbedo` | Cached name for the '_packedAlbedo' field. |
| `PropertyName._packedOrm` | Cached name for the '_packedOrm' field. |

## Methods

| Name | Summary |
|------|---------|
| `ClampSize(int, int)` | Scales (w,h) down to `MaxPackResolution`, preserving the aspect ratio. |
| `GetEffectiveAlbedo()` | Returns Albedo with merged Opacity if the Opacity slot is set, else the raw Albedo. Thread-safe. |
| `GetEffectiveOrm()` | Returns the auto-packed ORM texture from separate O/R/M channels. Cached and thread-safe. |
| `MergeAlbedoOpacity()` | Merges the Opacity texture's red channel into the Albedo alpha channel. |
| `NormalizedBytes(Texture2D, int, int, Image.Format)` | Returns a texture's raw pixel bytes in the requested format and size, decompressing/converting/resizing (Lanczos) as needed. |
| `PackOrm()` | Packs separate O/R/M textures into a single RGB image for the Godot decal ORM slot. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
