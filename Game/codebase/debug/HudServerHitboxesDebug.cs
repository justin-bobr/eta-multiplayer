using Godot;
using System.Collections.Generic;

/// <summary>
/// Debug-Renderer für die SERVER-Hitbox-Transforms (kommt via <see cref="PacketType.DebugHitboxes"/>
/// vom Server ~10Hz). Anders als <see cref="HitboxRig"/>'s grüne Wireframes (= LOKALE Puppet-Bones)
/// zeigt das hier wo + WIE die HITBOXEN AUF DEM SERVER aktuell sind — exakt das was der Server-Cast
/// in <see cref="PlayerCore.RunAuthoritativeHitscan"/> trifft, inkl. Shape-Größen + Rotation.
///
/// Shape-Größen werden aus dem PUPPET's HitboxRig gelesen (= identische Specs zum Server, dort wo
/// die Default-Shapes definiert sind). Position + Rotation kommt vom Server via Packet.
///
/// Aktiv nur wenn <see cref="ConVars.Sv.DebugHitboxes"/>. Pool von MeshInstance3D pro Agent + Index,
/// recycled. Mesh-Cache wird pro netId getrackt — wenn die HitboxRig-Reference wechselt (Respawn,
/// Disconnect+Reconnect), invalidiere ich den Cache + reuse die MeshInstances mit den neuen Meshes.
/// </summary>
public partial class HudServerHitboxesDebug : Node3D
{
	private class NetMarkerState
	{
		public readonly List<MeshInstance3D> Pool = new();
		public readonly List<Mesh> Meshes = new();
		// Tracked rig reference: wenn sich diese ändert (oder ein != null Wert nach null kommt),
		// invalidieren wir den Mesh-Cache + builden mit den neuen Rig-Specs neu.
		public HitboxRig LastRig;
	}

	private readonly Dictionary<byte, NetMarkerState> _states = new();
	private StandardMaterial3D _markerMat;

	public override void _Ready()
	{
		TopLevel = true;
		_markerMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 0.2f, 0.2f, 0.35f),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			NoDepthTest = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
	}

	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("HudServerHitboxesDebug._Process");
		// Default off — User aktiviert via console: sv_debug_hitboxes 1 (server-weit, alle Clients).
		if (!ConVars.Sv.DebugHitboxes)
		{
			foreach (var kv in _states)
				foreach (var m in kv.Value.Pool)
					if (GodotObject.IsInstanceValid(m)) m.Visible = false;
			return;
		}
		var client = NetMain.Instance?.Client;
		if (client == null) return;

		byte ownNetId = client.OwnNetId;
		var puppetMgr = NetMain.Instance?.Puppets;

		foreach (var kv in client.ServerHitboxTransforms)
		{
			byte netId = kv.Key;
			if (netId == ownNetId) continue;
			Transform3D[] transforms = kv.Value;
			if (transforms == null || transforms.Length == 0) continue;

			// Shape-Specs vom lokalen Puppet holen — gleiche DefaultSpecs Definition, gleiche Order
			// wie HitboxRig.HitboxNodes. Wenn Puppet noch nicht da → Markers stehen unsichtbar.
			HitboxRig puppetRig = null;
			if (puppetMgr != null && puppetMgr.Puppets.TryGetValue(netId, out var puppet))
			{
				var visualPc = puppet.GetVisualPlayerCore();
				puppetRig = visualPc?.GetHitboxRig();
			}

			if (!_states.TryGetValue(netId, out var state))
			{
				state = new NetMarkerState();
				_states[netId] = state;
			}

			// Rig-Ref Wechsel → Cache invalidieren. Bei Respawn/Reconnect ist's ein neues HitboxRig-
			// Objekt (gleiche Specs, aber neuer Hash). Stale Meshes wegwerfen, neu builden auf Demand.
			if (state.LastRig != puppetRig)
			{
				state.Meshes.Clear();
				state.LastRig = puppetRig;
			}

			// MeshInstance-Pool auf transforms.Length wachsen.
			while (state.Pool.Count < transforms.Length)
			{
				var mi = new MeshInstance3D
				{
					Name = $"sv_hb_{netId}_{state.Pool.Count}",
					MaterialOverride = _markerMat,
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
					TopLevel = true,
				};
				AddChild(mi);
				state.Pool.Add(mi);
			}

			// Mesh-Cache auf gleicher Länge halten. Lazy fill: pro Index erst beim ersten valid Rig.
			while (state.Meshes.Count < transforms.Length) state.Meshes.Add(null);

			for (int i = 0; i < transforms.Length; i++)
			{
				var mi = state.Pool[i];
				if (!GodotObject.IsInstanceValid(mi)) continue;

				// Mesh lazy bauen sobald puppetRig da ist + index in Range. Vorher (Rig fehlt): Marker
				// unsichtbar lassen statt Fallback-Sphere zu rendern — Fallback macht's verwirrend (User
				// denkt "Marker sind klein/falsch" obwohl's eigentlich "Marker noch nicht ready" ist).
				if (state.Meshes[i] == null && puppetRig != null && i < puppetRig.HitboxNodes.Count)
				{
					state.Meshes[i] = BuildMeshFromShape(puppetRig, i);
					mi.Mesh = state.Meshes[i];
				}
				if (state.Meshes[i] == null)
				{
					mi.Visible = false;
					continue;
				}
				// Defensiv: wenn Mesh-Cache vorhanden ist aber mi.Mesh aus irgendeinem Grund anders ist
				// (z.B. nach Cache-Invalidate, weil wir nur den Cache cleared aber Mesh-Property nicht).
				if (mi.Mesh != state.Meshes[i]) mi.Mesh = state.Meshes[i];

				mi.GlobalTransform = transforms[i];
				mi.Visible = true;
			}
			for (int i = transforms.Length; i < state.Pool.Count; i++)
				if (GodotObject.IsInstanceValid(state.Pool[i])) state.Pool[i].Visible = false;
		}

		// NetIds die nicht mehr im Dict sind (Spieler disconnected) → markers ausblenden.
		foreach (var kv in _states)
		{
			if (client.ServerHitboxTransforms.ContainsKey(kv.Key)) continue;
			foreach (var m in kv.Value.Pool)
				if (GodotObject.IsInstanceValid(m)) m.Visible = false;
		}
	}

	/// <summary>Mappt den Hitbox-Index auf das passende Mesh aus dem Puppet's HitboxRig (= identische
	/// Specs zum Server). NUR aufgerufen wenn puppetRig + index valid sind — kein Fallback nötig.</summary>
	private static Mesh BuildMeshFromShape(HitboxRig puppetRig, int index)
	{
		var hb = puppetRig.HitboxNodes[index];
		if (hb == null || !GodotObject.IsInstanceValid(hb)) return null;
		CollisionShape3D cs = null;
		foreach (Node ch in hb.GetChildren()) if (ch is CollisionShape3D c) { cs = c; break; }
		if (cs?.Shape == null) return null;
		return cs.Shape switch
		{
			CapsuleShape3D cap => new CapsuleMesh { Radius = cap.Radius, Height = cap.Height, RadialSegments = 8, Rings = 4 },
			SphereShape3D sph => new SphereMesh { Radius = sph.Radius, Height = sph.Radius * 2f, RadialSegments = 8, Rings = 4 },
			BoxShape3D box => new BoxMesh { Size = box.Size },
			_ => null,
		};
	}
}
