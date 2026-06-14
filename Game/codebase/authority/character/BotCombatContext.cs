using System.Collections.Generic;
using Godot;

namespace Vantix.Character;

public struct BotCombatContext
{
	public List<PeerState> AllPeers;
	public byte OwnNetId;
	public Team OwnTeam;
	public Rid SelfBodyRid;
	public int Difficulty;
	public int TickRate;

	/// <summary>True when the bot's magazine is empty and it isn't already reloading. Drives the
	/// per-tick ReloadPressed flag; the edge detector fires the reload once.</summary>
	public bool NeedsReload;
}
