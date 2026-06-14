# LutTexture3D

`Vantix.Fx.LutTexture3D`

Converts a 2D horizontal LUT strip into an ImageTexture3D and assigns it to the parent WorldEnvironment's color-correction slot.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.BuildTexture3D` | Cached name for the 'BuildTexture3D' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.LutSize` | Cached name for the 'LutSize' field. |
| `PropertyName.LutStrip` | Cached name for the 'LutStrip' field. |

## Methods

| Name | Summary |
|------|---------|
| `BuildTexture3D(Texture2D, int)` | Builds an ImageTexture3D by slicing a horizontal LUT strip into N square slices. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `_Ready()` | Builds the 3D LUT on scene load and assigns it to the parent WorldEnvironment. |
