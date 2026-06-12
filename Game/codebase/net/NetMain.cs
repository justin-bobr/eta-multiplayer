using Godot;

/// <summary>
/// Top-level netcode boot. Loaded as an autoload (see project.godot [autoload]) so it exists
/// before any scene is entered. Spawns <see cref="NetServer"/> and/or <see cref="NetClient"/>
/// per the parsed <see cref="NetCli"/>.
///
/// Polls LiteNetLib every physics tick with ProcessPriority = -100 so inputs and snapshots
/// arrive BEFORE <see cref="NetworkPlayer._PhysicsProcess"/>.
/// </summary>
public partial class NetMain : Node
{
	public static NetMain Instance { get; private set; }

	public NetCli Cli { get; private set; }
	public NetServer Server { get; private set; }
	public NetClient Client { get; private set; }
	public PuppetManager Puppets { get; private set; }

	/// <summary>The local player — instantiated by NetMain into the Players container after SpawnAck.</summary>
	public NetworkPlayer LocalPlayer { get; private set; }

	/// <summary>Public helper for other systems (Crosshair, DebugOverlay, NetClient.Reconcile) — returns <see cref="LocalPlayer"/>.</summary>
	public NetworkPlayer FindLocalPlayer() => LocalPlayer;

	private bool _localPlayerInitialized;

	/// <summary>Initialises the autoload — parses the CLI, applies settings. Server / Listen / auto-connect
	/// Client modes are started immediately; a Client without <c>--connect</c> waits for the main menu to
	/// call <see cref="ConnectToServer"/>.</summary>
	public override void _Ready()
	{
		Instance = this;
		ProcessPriority = -100;

		Cli = NetCli.Parse();

		if (Cli.Mode != NetMode.Server)
		{
			Settings.Load();
			Settings.ApplyDisplay();
		}
		else
		{
			Settings.ApplyServerHeadlessDefaults();
			Engine.MaxFps = Cli.TickRate;
		}
		Dbg.Print($"[NetMain] {Cli}");
		// SustainedLowLatency-Mode: GC delays Gen2 collections aggressively, eliminiert
		// die "100ms hitch" beim background-trigger. Wirkt mit Workstation UND Server GC.
		// Notwendig weil Godot+.NET (CoreCLR-host) die runtimeconfig.json's System.GC.Server
		// nicht respektiert — bleibt im Workstation-Mode trotz korrekt geschriebener config.
		// SustainedLowLatency ist die zweitbeste Option und C#-side anwendbar.
		System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
		GD.Print($"[Runtime] GC mode: {(System.Runtime.GCSettings.IsServerGC ? "Server GC ON" : "Workstation GC (CoreCLR-host ignores runtimeconfig)")}  latency: {System.Runtime.GCSettings.LatencyMode}");
		NetStats.Reset(Cli.Mode);

		switch (Cli.Mode)
		{
			case NetMode.Server:
				Server = new NetServer(Cli);
				Server.Start();
				break;

			case NetMode.Listen:
				Server = new NetServer(Cli);
				Server.Start();
				CreateAndStartClient();
				break;

			case NetMode.Client:
				if (Cli.AutoConnect)
					CreateAndStartClient();
				break;
		}
	}

	/// <summary>Called by the main menu's Connect button. Applies the entered host/port to the CLI,
	/// starts the network client and switches to the loading scene which drives the connect flow.</summary>
	public void ConnectToServer(string host, int port)
	{
		Cli.Mode = NetMode.Client;
		Cli.AutoConnect = true;
		Cli.Host = host;
		Cli.Port = port;
		NetStats.Reset(Cli.Mode);
		Dbg.Print($"[NetMain] ConnectToServer → {Cli.Host}:{Cli.Port}");
		CreateAndStartClient();
		GetTree().ChangeSceneToFile("res://loading.tscn");
	}

	/// <summary>Creates a new NetClient, attaches event subscribers and starts it. Called once from
	/// <see cref="_Ready"/> and again after a disconnect via <see cref="RequestReconnect"/>.</summary>
	private void CreateAndStartClient()
	{
		Client = new NetClient(Cli);
		Client.OnSpawned += OnClientSpawned;
		Client.OnDisconnected += HandleDisconnect;
		Client.Start();
	}

	/// <summary>Called from <see cref="NetClient"/> as soon as SpawnAck has arrived.</summary>
	private void OnClientSpawned()
	{
		_localPlayerInitialized = false;
	}

	private PackedScene _characterScene;

	/// <summary>Instantiates the local player into the Players container once SpawnAck has arrived AND world.tscn is active.</summary>
	private bool _teamSelectFlowInitialized;
	/// <summary>One-shot: when the client lands in Spectator team after SpawnAck (= competitive mode,
	/// no spawn yet), spawn the PreviewCameraController (cinematic cycle through map angles) and
	/// the TeamSelectionMenu (CS-style CT/T picker). Both self-destruct once SpawnAuthorize arrives
	/// and the LocalPlayer instantiates. Skipped for deathmatch (already authorized → LocalPlayer
	/// spawned directly).</summary>
	private void TryInitializeTeamSelectFlow()
	{
		if (_teamSelectFlowInitialized || Client == null || !Client.Spawned) return;
		if (Client.SpawnAuthorized) { _teamSelectFlowInitialized = true; return; }
		var tree = GetTree();
		if (tree?.CurrentScene == null || tree.CurrentScene.Name != "World") return;
		_teamSelectFlowInitialized = true;
		tree.CurrentScene.AddChild(new PreviewCameraController { Name = "PreviewCameraController" });
		tree.CurrentScene.AddChild(new TeamSelectionMenu { Name = "TeamSelectionMenu" });
		Dbg.Print("[NetMain] Spectator team → PreviewCameraController + TeamSelectionMenu spawned");
	}

	private void TryInitializeLocalPlayer()
	{
		if (_localPlayerInitialized || Client == null || !Client.Spawned)
			return;
		// Competitive mode: client lands here in Spectator team with SpawnAuthorized=false.
		// Don't instantiate the LocalPlayer scene yet — the PreviewCameraController + team-
		// select menu run in the meantime. Once the user picks a team and the server replies
		// with SpawnAuthorize, SpawnAuthorized flips true and this gate finally lets us through.
		if (!Client.SpawnAuthorized)
			return;
		var tree = GetTree();
		if (tree?.CurrentScene == null)
			return;
		if (tree.CurrentScene.Name != "World")
			return;
		var playersContainer = tree.CurrentScene.GetNodeOrNull<Node3D>("Players");
		if (playersContainer == null)
		{
			GD.PushError("[NetMain] World/Players Node3D missing — cannot spawn LocalPlayer");
			return;
		}

		if (LocalPlayer != null && GodotObject.IsInstanceValid(LocalPlayer))
		{
			Dbg.Print("[NetMain] Cleaning up old LocalPlayer instance (reconnect)");
			LocalPlayer.QueueFree();
			LocalPlayer = null;
		}
		if (Puppets != null)
		{
			Puppets.Shutdown();
			Puppets = null;
		}

		_characterScene ??= GD.Load<PackedScene>("res://character/local_player.tscn");
		var local = _characterScene.Instantiate<NetworkPlayer>();
		local.CurrentGameMode = PresentationMode.Local;
		local.NetId = Client.OwnNetId;
		local.Name = $"local_{Client.OwnNetId}";
		local.Position = Client.PendingSpawnPos;
		var rot = local.Rotation;
		rot.Y = Client.PendingSpawnYaw;
		local.Rotation = rot;
		playersContainer.AddChild(local);
		LocalPlayer = local;
		local.ResetInterpToCurrentPos();
		// Weapon viewmodel renders in its own own_world_3d SubViewport, so it inherits NONE of the
		// level's look. Calibrate it to the loaded map: copy the level env's tonemap/grade/glow/
		// ambient onto viewmodel_env (Sync, BEFORE Attach so the world-env finder isn't fooled by
		// the compositor we add next), then give the viewmodel its own Compositor so screen-space
		// post-FX (CA/sharpen/vignette/grain/MB) reach the weapon too.
		ViewmodelMotionBlur.Reset();
		ViewmodelEnvSync.Sync(local, tree);
		ViewmodelMotionBlur.Attach(local);
		// The compositor.tres ships with enabled=false (scene-default); Settings.Apply()
		// is what flips it on plus pushes the MotionBlur/Vignette/Grain toggles down.
		// On the server this is called from NetServer once the world is loaded; on the
		// client it would otherwise only run when the user opens the Settings menu and
		// changes something → PostProcessEffect stays inert and no motion blur is ever
		// produced. Trigger one apply here so the initial state is consistent.
		Settings.Apply(tree);
		Dbg.Print(
			$"[NetMain] LocalPlayer spawned: netId={local.NetId} at {local.GlobalPosition} yaw={local.Rotation.Y:F2}"
		);

		Puppets = new PuppetManager();
		Puppets.Init(playersContainer, Client);
		Dbg.Print("[NetMain] PuppetManager initialized");

		var scoreboard = new Scoreboard();
		tree.CurrentScene.AddChild(scoreboard);
		Dbg.Print("[NetMain] Scoreboard attached (Tab to toggle)");

		// HitFeed (top-center "YOU → (CHEST) → Player 2 (-25 → 25 HP)") entfernt — redundant zum
		// HudHitmarker der nur kompakt "-25 HP HEADSHOT" neben dem Crosshair zeigt (CoD-Style).
		// Klassen-Datei HitFeed.cs bleibt fürs etwaige Wiederaufnehmen.

		// HP-System HUD-Stack — hitmarker (eigene Damage-Pop-Ups), killfeed (alle Deaths), low-hp screen-fx.
		// Alle drei abonnieren NetClient-Events bzw. lesen LastSelfSnap. Layers werden in HudGate
		// registriert, der ihre Visibility ein/ausschaltet basierend auf LocalPlayer-Live-State
		// (versteckt während team-select / preload / Hp=0).
		var hitmarkerLayer = new CanvasLayer { Name = "hitmarker_layer", Layer = 110 };
		tree.CurrentScene.AddChild(hitmarkerLayer);
		hitmarkerLayer.AddChild(new HudHitmarker { Name = "HudHitmarker" });
		HudGate.Register(hitmarkerLayer);

		var killfeedLayer = new CanvasLayer { Name = "killfeed_layer", Layer = 110 };
		tree.CurrentScene.AddChild(killfeedLayer);
		killfeedLayer.AddChild(new HudKillfeed { Name = "HudKillfeed" });
		HudGate.Register(killfeedLayer);

		var lowhpLayer = new CanvasLayer { Name = "lowhp_layer", Layer = 105 };
		tree.CurrentScene.AddChild(lowhpLayer);
		lowhpLayer.AddChild(new HudLowHpFx { Name = "HudLowHpFx" });
		HudGate.Register(lowhpLayer);

		// Canvas-stage post-FX (CA/Sharpen/Vignette/Grain) at layer 50 — between viewmodel
		// (layer 10) and HUD (layer 100+). FSR2-compatible alternative to the Compositor-
		// based PostProcessEffect; Settings.ApplyEffects toggles which one is active.
		tree.CurrentScene.AddChild(new PostCanvasFx { Name = "PostCanvasFx" });

		// Dev-Console — Quake/CS-Style Bottom-Panel mit Hotkey ^ (oder ` US-Layout). Liest local
		// + sv_* ConVars; sv_* werden in Phase 2 via ConVarSync-Packet an Server geschickt.
		tree.CurrentScene.AddChild(new ConsoleHud { Name = "ConsoleHud" });

		// Debug-Visualizations — IMMER gespawnt, intern via sv_debug_* ConVars toggle'd.
		// Spawn-Cost ist minimal (Node3D + paar Meshes), Sichtbarkeit + Server-Broadcasts werden
		// via die ConVarSync-Pipeline gated. So kann der User per Console live ein/ausschalten ohne
		// Reconnect, und nach Reconnect wird der Sv-State via SendInitialConVarSync zurückgesynct.
		tree.CurrentScene.AddChild(new ServerAimRayDebug { Name = "ServerAimRayDebug" });
		tree.CurrentScene.AddChild(new ServerBodyCapsuleDebug { Name = "ServerBodyCapsuleDebug" });
		tree.CurrentScene.AddChild(new HudServerHitboxesDebug { Name = "HudServerHitboxesDebug" });

		// Mini-Profiler HUD — togglet via cl_profiler 1. Zeigt Top-Methoden nach proc time + warns
		// bei Samples > 5ms. Wrap Methoden mit `using var _ = MiniProfiler.Sample("Name")`.
		tree.CurrentScene.AddChild(new HudMiniProfiler { Name = "HudMiniProfiler" });

		// BulletTracerPool — MultiMesh-Pool für alle Bullet-Tracer. Single draw call statt per-Tracer
		// Node3D+Mesh+Material allocation (= 5.6ms first-fire spike → 0.05ms). LocalAnimation
		// .TriggerBulletTracer ruft Pool.Emit statt BulletTracer.Spawn.
		tree.CurrentScene.AddChild(new BulletTracerPool { Name = "BulletTracerPool" });

		_localPlayerInitialized = true;
	}

	/// <summary>Pumps server and client every physics tick and lazily spawns the local player when ready.</summary>
	public override void _PhysicsProcess(double delta)
	{
		using var _prof = MiniProfiler.Sample("NetMain._PhysicsProcess (both)");
		Server?.Poll();
		using (MiniProfiler.SampleClient("NetClient.Poll")) Client?.Poll();

		if (!_localPlayerInitialized)
		{
			TryInitializeLocalPlayer();
			TryInitializeTeamSelectFlow();
		}
		HudGate.Tick();
	}

	// Spike-Logger — nur aktiv wenn Dbg.Enabled (= ProjectSettings "global/debug" auf true). Sonst
	// rebnaiert _Process ein no-op. Hot-Path schnell: nur dt-Check, teure Performance.GetMonitor +
	// String-Format passieren ausschließlich on-spike.
	private const double SpikeThresholdSec = 0.030;
	private int _gen0Last, _gen1Last, _gen2Last;
	private long _heapLast;
	private bool _spikeTrackerInited;
	private long _drawCallsLast, _objCountLast, _nodeCountLast, _orphanLast;
	private long _physActiveLast, _physPairsLast, _physIslandsLast;
	private long _vramLast;
	private double _timeProcessLast, _timePhysProcessLast;

	public override void _Process(double delta)
	{
		if (!Dbg.Enabled) return;
		TrackFrameSpike(delta);
	}

	/// <summary>Logs Frame-Spikes > <see cref="SpikeThresholdSec"/> ms zu GD.Print mit GC-Stats +
	/// Godot-Performance-Deltas. Gated auf Dbg.Enabled (siehe <see cref="_Process"/>) damit Production-
	/// Build keinen Overhead trägt.</summary>
	private void TrackFrameSpike(double delta)
	{
		if (!_spikeTrackerInited)
		{
			_spikeTrackerInited = true;
			_gen0Last = System.GC.CollectionCount(0);
			_gen1Last = System.GC.CollectionCount(1);
			_gen2Last = System.GC.CollectionCount(2);
			_heapLast = System.GC.GetTotalMemory(forceFullCollection: false);
			return;
		}

		if (delta < SpikeThresholdSec) return;

		long drawCalls = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
		long objCount = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount);
		long nodeCount = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
		long orphan = (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
		long physActive = (long)Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects);
		long physPairs = (long)Performance.GetMonitor(Performance.Monitor.Physics3DCollisionPairs);
		long physIslands = (long)Performance.GetMonitor(Performance.Monitor.Physics3DIslandCount);
		long vram = (long)Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed);
		double timeProc = Performance.GetMonitor(Performance.Monitor.TimeProcess);
		double timePhys = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess);

		int gen0 = System.GC.CollectionCount(0);
		int gen1 = System.GC.CollectionCount(1);
		int gen2 = System.GC.CollectionCount(2);
		long heap = System.GC.GetTotalMemory(forceFullCollection: false);
		int dGen0 = gen0 - _gen0Last;
		int dGen1 = gen1 - _gen1Last;
		int dGen2 = gen2 - _gen2Last;
		long dHeapKb = (heap - _heapLast) / 1024;
		long heapKb = heap / 1024;
		string gcTag = dGen2 > 0 ? " [GC-GEN2]" : dGen1 > 0 ? " [GC-GEN1]" : dGen0 > 0 ? " [GC-GEN0]" : "";

		long dDraw = drawCalls - _drawCallsLast;
		long dObj = objCount - _objCountLast;
		long dNode = nodeCount - _nodeCountLast;
		long dOrphan = orphan - _orphanLast;
		long dPhysActive = physActive - _physActiveLast;
		long dPhysPairs = physPairs - _physPairsLast;
		long dPhysIslands = physIslands - _physIslandsLast;
		long dVramKb = (vram - _vramLast) / 1024;
		double dProc = (timeProc - _timeProcessLast) * 1000;
		double dPhys = (timePhys - _timePhysProcessLast) * 1000;

		string roleTag = Cli?.Mode switch
		{
			NetMode.Server => "[SV]",
			NetMode.Client => $"[CL netId={LocalPlayer?.NetId.ToString() ?? "?"}]",
			NetMode.Listen => $"[HOST netId={LocalPlayer?.NetId.ToString() ?? "?"}]",
			_ => "[?]",
		};

		GD.Print(
			$"[SPIKE]{roleTag} dt={delta * 1000:F1}ms{gcTag} | gc Δ gen0={dGen0} gen1={dGen1} gen2={dGen2} heap={heapKb}KB (Δ {dHeapKb:+0;-0;0}KB)\n" +
			$"  godot: process={timeProc * 1000:F2}ms phys={timePhys * 1000:F2}ms (Δ {dProc:+0.0;-0.0;0}/{dPhys:+0.0;-0.0;0}ms)\n" +
			$"  render: draw={drawCalls} (Δ {dDraw:+0;-0;0}) vram={vram / (1024 * 1024)}MB (Δ {dVramKb:+0;-0;0}KB)\n" +
			$"  scene: objects={objCount} (Δ {dObj:+0;-0;0}) nodes={nodeCount} (Δ {dNode:+0;-0;0}) orphans={orphan} (Δ {dOrphan:+0;-0;0})\n" +
			$"  physics: active={physActive} (Δ {dPhysActive:+0;-0;0}) pairs={physPairs} (Δ {dPhysPairs:+0;-0;0}) islands={physIslands} (Δ {dPhysIslands:+0;-0;0})");

		_gen0Last = gen0; _gen1Last = gen1; _gen2Last = gen2; _heapLast = heap;
		_drawCallsLast = drawCalls; _objCountLast = objCount; _nodeCountLast = nodeCount;
		_orphanLast = orphan; _physActiveLast = physActive; _physPairsLast = physPairs;
		_physIslandsLast = physIslands; _vramLast = vram;
		_timeProcessLast = timeProc; _timePhysProcessLast = timePhys;
	}

	/// <summary>Called by the NetClient when the transport drops (server shutdown / timeout / kick).
	/// Tears the World state down and shows the <see cref="DisconnectScreen"/> overlay; its Reconnect
	/// button calls <see cref="RequestReconnect"/>.</summary>
	private DisconnectScreen _disconnectScreen;

	/// <summary>User-initiated disconnect (Settings menu button). Stops the NetClient (closes
	/// the socket) and routes through the same cleanup path the transport-drop callback uses, so
	/// LocalPlayer/Puppets/HudGate get torn down and the DisconnectScreen overlay shows up.
	/// Subscription to <see cref="NetClient.OnDisconnected"/> is removed first so the cleanup
	/// only runs once even if LiteNetLib's Stop() also fires a peer-disconnect event.</summary>
	public void RequestDisconnect(string reason = "Disconnected by user")
	{
		Dbg.Print($"[NetMain] RequestDisconnect: {reason}");
		if (Client != null)
		{
			Client.OnDisconnected -= HandleDisconnect;
			Client.Stop();
		}
		HandleDisconnect(reason);
	}

	/// <summary>True while we're in the post-disconnect idle state — set after a disconnect, cleared
	/// when the user picks Reconnect/Quit. SceneLoader checks this to suppress its auto-connect logic
	/// (= sits idle behind the DisconnectScreen overlay rather than firing a fresh connect attempt).</summary>
	public static bool PostDisconnectIdle;

	private void HandleDisconnect(string reason)
	{
		Dbg.Print($"[NetMain] HandleDisconnect: {reason}");
		if (LocalPlayer != null && GodotObject.IsInstanceValid(LocalPlayer))
		{
			LocalPlayer.QueueFree();
			LocalPlayer = null;
		}
		Puppets?.Shutdown();
		Puppets = null;
		_localPlayerInitialized = false;
		_teamSelectFlowInitialized = false;
		HudGate.Reset();

		// Switch the rendered scene back to loading.tscn — this frees the whole world tree (HUDs,
		// LocalPlayer remnants, ServerAgents, World3D) in one go. Without this, HUD nodes that
		// hold a ref to the freed LocalPlayer crash with ObjectDisposedException on the next
		// _Process. The DisconnectScreen overlay below is added to GetTree().Root (NOT to the
		// current scene), so it survives the scene change and remains visible on top.
		PostDisconnectIdle = true;
		Cli.AutoConnect = false;
		GetTree().ChangeSceneToFile("res://loading.tscn");

		if (_disconnectScreen != null && GodotObject.IsInstanceValid(_disconnectScreen))
		{
			var oldParent = _disconnectScreen.GetParent();
			if (oldParent != null && GodotObject.IsInstanceValid(oldParent))
				oldParent.QueueFree();
			_disconnectScreen = null;
		}
		var layer = new CanvasLayer { Layer = 1000, Name = "disconnect_overlay" };
		GetTree().Root.AddChild(layer);
		_disconnectScreen = new DisconnectScreen { Reason = reason };
		layer.AddChild(_disconnectScreen);
	}

	/// <summary>Called by the <see cref="DisconnectScreen"/> reconnect button: tears the old client
	/// down, spins up a fresh one, and re-enters loading.tscn which drives the normal Connect →
	/// Handshake → Spawn flow again.</summary>
	public void RequestReconnect()
	{
		Dbg.Print("[NetMain] Reconnect requested");
		if (_disconnectScreen != null && GodotObject.IsInstanceValid(_disconnectScreen))
		{
			var parent = _disconnectScreen.GetParent();
			if (parent != null && GodotObject.IsInstanceValid(parent))
				parent.QueueFree();
			_disconnectScreen = null;
		}

		if (Client != null)
		{
			Client.Stop();
			Client = null;
		}
		// Re-enable auto-connect path before recreating the client + reloading the scene so
		// SceneLoader's Connecting phase actually progresses instead of sitting idle.
		PostDisconnectIdle = false;
		Cli.AutoConnect = true;
		CreateAndStartClient();

		GetTree().ChangeSceneToFile("res://loading.tscn");
	}

	/// <summary>Tears down networking resources on shutdown.</summary>
	public override void _ExitTree()
	{
		Puppets?.Shutdown();
		Server?.Stop();
		Client?.Stop();
		Instance = null;
	}
}
