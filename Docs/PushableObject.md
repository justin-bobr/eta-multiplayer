# PushableObject

Pushable `RigidBody3D` (e.g. a car). The player holds the push action and presses against it; after `MinChargeSeconds` of sustained contact aligned with `PushDirection`, force is applied each physics tick. A 2D prompt shows when the player is close behind. Editor [Tool] mode draws a yellow direction arrow (not saved). Client-local only.

## Fields

| Name | Summary |
|------|---------|
| `MethodName.DescribeContacts` | Cached name for the 'DescribeContacts' method. |
| `MethodName.EnsurePrompt` | Cached name for the 'EnsurePrompt' method. |
| `MethodName.FindNetworkPlayer` | Cached name for the 'FindNetworkPlayer' method. |
| `MethodName.IsBeingPushed` | Cached name for the 'IsBeingPushed' method. |
| `MethodName.KeyName` | Cached name for the 'KeyName' method. |
| `MethodName.ResolvePlayer` | Cached name for the 'ResolvePlayer' method. |
| `MethodName.UpdateDirectionGizmo` | Cached name for the 'UpdateDirectionGizmo' method. |
| `MethodName.UpdatePrompt` | Cached name for the 'UpdatePrompt' method. |
| `MethodName._PhysicsProcess` | Cached name for the '_PhysicsProcess' method. |
| `MethodName._Process` | Cached name for the '_Process' method. |
| `MethodName._Ready` | Cached name for the '_Ready' method. |
| `PropertyName.AlignThreshold` | Cached name for the 'AlignThreshold' field. |
| `PropertyName.ChargeDecayRate` | Cached name for the 'ChargeDecayRate' field. |
| `PropertyName.MaxPushDistance` | Cached name for the 'MaxPushDistance' field. |
| `PropertyName.MinChargeSeconds` | Cached name for the 'MinChargeSeconds' field. |
| `PropertyName.PromptFontSize` | Cached name for the 'PromptFontSize' field. |
| `PropertyName.PromptRange` | Cached name for the 'PromptRange' field. |
| `PropertyName.PromptText` | Cached name for the 'PromptText' field. |
| `PropertyName.PushAction` | Cached name for the 'PushAction' field. |
| `PropertyName.PushDirection` | Cached name for the 'PushDirection' field. |
| `PropertyName.PushForce` | Cached name for the 'PushForce' field. |
| `PropertyName._charge` | Cached name for the '_charge' field. |
| `PropertyName._dirGizmo` | Cached name for the '_dirGizmo' field. |
| `PropertyName._hasStart` | Cached name for the '_hasStart' field. |
| `PropertyName._nextDiagAt` | Cached name for the '_nextDiagAt' field. |
| `PropertyName._player` | Cached name for the '_player' field. |
| `PropertyName._promptLabel` | Cached name for the '_promptLabel' field. |
| `PropertyName._promptLayer` | Cached name for the '_promptLayer' field. |
| `PropertyName._startPos` | Cached name for the '_startPos' field. |
| `PropertyName._wakeRecheckTickCounter` | Cached name for the '_wakeRecheckTickCounter' field. |
| `PropertyName._wasMoving` | Cached name for the '_wasMoving' field. |

## Methods

| Name | Summary |
|------|---------|
| `DescribeContacts(NetworkPlayer, Vector3)` | Diagnostic helper: lists each slide collision of the player along with its dot to PushDirection. |
| `EnsurePrompt()` | Lazily builds the 2D hint (CanvasLayer + Label, anchored bottom-centre). |
| `FindNetworkPlayer(Node)` | Recursive depth-first search returning the first NetworkPlayer under `n`. |
| `IsBeingPushed(NetworkPlayer, Vector3)` | True when the player holds the push action and presses against this body along PushDirection. |
| `KeyName()` | Resolves the key/button bound to `PushAction` in the Input Map for the on-screen prompt. |
| `ResolvePlayer()` | Looks up the local player once via a tree search and caches the reference. |
| `RestoreGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `SaveGodotObjectData(Bridge.GodotSerializationInfo)` | — |
| `UpdateDirectionGizmo()` | Editor visual: draws a yellow arrow along `PushDirection` with length `MaxPushDistance`. The arrow node has no owner, so it is not saved to the scene. |
| `UpdatePrompt(NetworkPlayer, Vector3, bool)` | Shows the 2D hint when the player is close enough behind the object. |
| `_PhysicsProcess(double)` | Runs the per-tick push logic: detects contact, builds charge, applies force when ready. |
| `_Process(double)` | Editor-only: keeps the direction arrow in sync with the live PushDirection value. |
| `_Ready()` | Caches the spawn position for the max-push-distance limit; builds the editor gizmo on scene open. |
