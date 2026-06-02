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
/// AI: each bot carries its own <see cref="BotController"/> that produces an <see cref="InputPacket"/>
/// per server tick. <see cref="NetServer.SpawnBot"/> calls <see cref="BotController.Init"/> once at
/// spawn; <see cref="NetServer.Poll"/> calls <see cref="BotController.Tick"/> every tick before
/// FeedInputsToAgents so the next physics step reads the fresh input. Plain field — no scene-graph
/// hop, no per-tick wrapper allocations.
/// </summary>
public partial class ServerBotPlayer : ServerPlayer
{
	public readonly BotController BotController = new();
}
