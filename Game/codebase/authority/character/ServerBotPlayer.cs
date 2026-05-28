using Godot;

/// <summary>
/// Server-authority body for a bot. No NetPeer behind it — the bot-specific spawn logic
/// (NetInputSource = zero-input with correct ViewYaw so BuildInput does NOT fall back to the live
/// input path and read the host keyboard) is set by <see cref="NetServer.SpawnBot"/> directly after
/// AddChild.
///
/// Inherits everything from <see cref="ServerPlayer"/> (sim path is identical — only that
/// NetInputSource stays static instead of being updated per tick from a peer).
///
/// Spawn / Despawn / PendingRemoval logic lives in <see cref="NetServer"/>. Bots can be killed by
/// friendly fire (no team check during damage resolve).
///
/// Future AI extension: hook a custom BotInputPolicy here (walk, aim, fire pattern) — write to
/// <see cref="ServerBaseCharacter.NetInputSource"/> per tick.
/// </summary>
public partial class ServerBotPlayer : ServerPlayer
{
}
