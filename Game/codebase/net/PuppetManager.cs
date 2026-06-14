using Godot;
using System.Collections.Generic;

/// <summary>Owned by <see cref="NetMain"/> in client modes (Listen + Client). Instantiates a
/// <see cref="PuppetPlayer"/> per remote NetId, feeds it snapshot state, and removes it on PlayerLeft.</summary>
public class PuppetManager
{
	private readonly Dictionary<byte, PuppetPlayer> _puppets = new();
	private Node3D _container;
	private NetClient _client;
	private static PackedScene _puppetScene;

	public IReadOnlyDictionary<byte, PuppetPlayer> Puppets => _puppets;

	/// <summary>Binds the manager to a container and a NetClient and replays any pre-init known players.</summary>
	public void Init(Node3D container, NetClient client)
	{
		_container = container;
		_client = client;
		_client.OnSnapshot += OnSnapshot;
		_client.OnPlayerJoined += OnPlayerJoined;
		_client.OnPlayerLeft += OnPlayerLeft;
		ReplayInitial();
	}

	/// <summary>Spawns puppets for any remote players that were already known before initialisation.</summary>
	private void ReplayInitial()
	{
		if (_client == null || _container == null) return;
		foreach (var kv in _client.RemotePlayers)
		{
			EnsurePuppet(kv.Key, kv.Value.PlayerName, kv.Value.Position);
		}
	}

	/// <summary>Pushes the latest snapshot data into each remote puppet, instantiating new ones as needed.</summary>
	private void OnSnapshot()
	{
		if (_container == null || _client == null) return;
		foreach (var kv in _client.LastRemoteSnapshots)
		{
			byte id = kv.Key;
			var puppet = EnsurePuppet(id, null, kv.Value.Pos);
			if (puppet != null) puppet.PushSnapshot(_client.LastSnapshotServerTick, kv.Value);
		}
	}

	/// <summary>Creates a puppet for a newly joined remote player.</summary>
	private void OnPlayerJoined(InitialPlayerState p)
	{
		EnsurePuppet(p.NetId, p.PlayerName, p.Position);
	}

	/// <summary>Removes the puppet for a player that has left.</summary>
	private void OnPlayerLeft(byte netId, LeaveReason reason)
	{
		if (_puppets.TryGetValue(netId, out var p))
		{
			if (GodotObject.IsInstanceValid(p)) p.QueueFree();
			_puppets.Remove(netId);
			Dbg.Print($"[PuppetManager] Removed puppet netId={netId}");
		}
	}

	/// <summary>Returns the puppet for the given NetId, creating and adding it to the container if necessary.</summary>
	private PuppetPlayer EnsurePuppet(byte netId, string nameHint, Vector3 initialPos)
	{
		if (_puppets.TryGetValue(netId, out var existing)) return existing;
		if (_container == null) return null;
		_puppetScene ??= GD.Load<PackedScene>("res://character/puppet_player.tscn");
		var p = _puppetScene.Instantiate<PuppetPlayer>();
		p.NetId = netId;
		p.PlayerName = nameHint ?? "";
		p.Name = $"puppet_{netId}";
		_container.AddChild(p);
		p.GlobalPosition = initialPos;
		_puppets[netId] = p;
		Dbg.Print($"[PuppetManager] Spawned puppet netId={netId} name=\"{p.PlayerName}\" at {initialPos}");
		return p;
	}

	/// <summary>Unsubscribes from the client and frees all spawned puppets.</summary>
	public void Shutdown()
	{
		if (_client != null)
		{
			_client.OnSnapshot -= OnSnapshot;
			_client.OnPlayerJoined -= OnPlayerJoined;
			_client.OnPlayerLeft -= OnPlayerLeft;
		}
		foreach (var p in _puppets.Values)
			if (GodotObject.IsInstanceValid(p)) p.QueueFree();
		_puppets.Clear();
	}
}
