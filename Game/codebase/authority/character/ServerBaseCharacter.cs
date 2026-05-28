using Godot;

/// <summary>
/// Foundation for server-side authority bodies — shared state for <see cref="PlayerCore"/>
/// (also used as LocalPlayer-mode during migration), <see cref="ServerPlayer"/> (driven by a real
/// peer), and <see cref="ServerBotPlayer"/> (bot, no input).
///
/// Holds:
///   - <see cref="NetInputSource"/> (set by NetServer/SpawnBot)
///   - <see cref="IsFrozen"/> (reconnect pool)
///   - <see cref="LastAppliedInputTick"/> (ackedInputTick for snapshot)
///   - <see cref="AuthorityPosition"/> (physics state from the end of the last tick, drift-free)
///
/// Layer convention: server bodies use layer 5 (overrides BaseCharacter default layer 2).
/// Subclasses may override again (PlayerCore does so flag-based: ServerAgent -> layer 5, else layer 2).
/// </summary>
public partial class ServerBaseCharacter : BaseCharacter
{
	/// <summary>When set, the movement sim reads from this instead of the live input singleton.
	/// ServerPlayer: filled per tick by NetServer. ServerBotPlayer: set once at spawn.</summary>
	public InputPacket? NetInputSource;

	/// <summary>Frozen state (reconnect pool): _PhysicsProcess returns immediately and the pose stays.
	/// CollisionLayer/Mask are nulled so live players do not get stuck on the ghost body. Only
	/// relevant for real player ServerAgents (bots do not disconnect).</summary>
	public bool IsFrozen
	{
		get => _isFrozen;
		set
		{
			if (_isFrozen == value) return;
			_isFrozen = value;
			if (value)
			{
				_savedCollisionLayer = CollisionLayer;
				_savedCollisionMask = CollisionMask;
				CollisionLayer = 0u;
				CollisionMask = 0u;
			}
			else
			{
				if (_savedCollisionLayer != 0u) CollisionLayer = _savedCollisionLayer;
				if (_savedCollisionMask != 0u) CollisionMask = _savedCollisionMask;
			}
		}
	}
	private bool _isFrozen;
	private uint _savedCollisionLayer;
	private uint _savedCollisionMask;

	/// <summary>Death-State: kein Movement, keine Collision, kein Schießen. Wird vom NetServer beim
	/// HP=0-Trigger gesetzt und beim Respawn wieder gelöscht. Nutzt dieselbe Collision-Zero-Logik wie
	/// <see cref="IsFrozen"/> damit der tote Body keinen lebenden Spieler blockt.
	/// Bots/echte Peers: PlayerCore.FixedTick checked das Flag und skippt seine Sim.</summary>
	public bool IsDead
	{
		get => _isDead;
		set
		{
			if (_isDead == value) return;
			_isDead = value;
			if (value)
			{
				if (_savedCollisionLayerDead == 0u && _savedCollisionMaskDead == 0u)
				{
					_savedCollisionLayerDead = CollisionLayer;
					_savedCollisionMaskDead = CollisionMask;
				}
				CollisionLayer = 0u;
				CollisionMask = 0u;
				Velocity = Vector3.Zero;
			}
			else
			{
				if (_savedCollisionLayerDead != 0u) CollisionLayer = _savedCollisionLayerDead;
				if (_savedCollisionMaskDead != 0u) CollisionMask = _savedCollisionMaskDead;
				_savedCollisionLayerDead = 0u;
				_savedCollisionMaskDead = 0u;
			}
		}
	}
	private bool _isDead;
	private uint _savedCollisionLayerDead;
	private uint _savedCollisionMaskDead;

	/// <summary>Tick index of the last consumed input. Sent back to the client as ackedTick so it can
	/// reconcile its prediction.</summary>
	public uint LastAppliedInputTick;

	/// <summary>Authoritative position for snapshot broadcast — physics state at the end of the last
	/// tick, NOT the currently rendered lerp value (avoids sub-cm drift on every snapshot).
	/// Implemented as a virtual property (was a raw field) so <see cref="PlayerCore"/> can override
	/// it to read from _currentPhysicsPos. Otherwise PeerState.ServerAgent (typed as
	/// ServerBaseCharacter) would read the base field instead of the derived logic, causing
	/// snapshots with Pos=(0,0,0) and ~46m client drift on reconcile (= spawn->origin distance).</summary>
	public virtual Vector3 AuthorityPosition { get; set; }

	/// <summary>Default: layer 5 (bit 4) for ServerAgents, masks world plus other ServerAgents.
	/// Body capsules of LocalPlayer/Puppet (layer 2) are not masked — no cross push.
	/// PlayerCore overrides this again (flag-based during the multi-role phase).</summary>
	protected override void ConfigureCollisionLayers()
	{
		CollisionLayer = 1u << 4;
		CollisionMask = 1u | (1u << 4);
	}
}
