# PuppetManager

`Vantix.Client.PuppetManager`

Owned by `NetMain` in client modes (Listen + Client). Instantiates a `PuppetPlayer` per remote NetId, feeds it snapshot state, and removes it on PlayerLeft.

## Methods

| Name | Summary |
|------|---------|
| `EnsurePuppet(byte, string, Vector3)` | Returns the puppet for the given NetId, creating and adding it to the container if necessary. |
| `Init(Node3D, Vantix.Client.NetClient)` | Binds the manager to a container and a NetClient and replays any pre-init known players. |
| `OnPlayerJoined(Vantix.Net.InitialPlayerState)` | Creates a puppet for a newly joined remote player. |
| `OnPlayerLeft(byte, Vantix.Net.LeaveReason)` | Removes the puppet for a player that has left. |
| `OnSnapshot()` | Pushes the latest snapshot data into each remote puppet, instantiating new ones as needed. |
| `ReplayInitial()` | Spawns puppets for any remote players that were already known before initialisation. |
| `Shutdown()` | Unsubscribes from the client and frees all spawned puppets. |
