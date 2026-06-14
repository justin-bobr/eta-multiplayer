using Godot;
using System.Collections.Generic;

/// <summary>
/// Renders the server's hitbox transforms (from <see cref="PacketType.DebugHitboxes"/> at ~10 Hz) as
/// what <see cref="ServerPlayer.RunAuthoritativeHitscan"/> actually casts: shape, size, position,
/// rotation. Shapes are read from the puppet's HitboxRig (identical specs); position/rotation from the
/// packet. Active only when <see cref="ConVars.Sv.DebugHitboxes"/>; pools MeshInstance3D per agent,
/// invalidating the per-netId mesh cache when the HitboxRig reference changes (respawn/reconnect).
/// </summary>
public partial class HudServerHitboxesDebug : Node3D
{
	private class NetMarkerState
	{
		public readonly List<MeshInstance3D> Pool = new();
		public readonly List<Mesh> Meshes = new();
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

			HitboxRig puppetRig = null;
			if (puppetMgr != null && puppetMgr.Puppets.TryGetValue(netId, out var puppet))
			{
				var visualPc = puppet.GetVisual();
				puppetRig = visualPc?.GetHitboxRig();
			}

			if (!_states.TryGetValue(netId, out var state))
			{
				state = new NetMarkerState();
				_states[netId] = state;
			}

			if (state.LastRig != puppetRig)
			{
				state.Meshes.Clear();
				state.LastRig = puppetRig;
			}

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

			while (state.Meshes.Count < transforms.Length) state.Meshes.Add(null);

			for (int i = 0; i < transforms.Length; i++)
			{
				var mi = state.Pool[i];
				if (!GodotObject.IsInstanceValid(mi)) continue;

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
				if (mi.Mesh != state.Meshes[i]) mi.Mesh = state.Meshes[i];

				mi.GlobalTransform = transforms[i];
				mi.Visible = true;
			}
			for (int i = transforms.Length; i < state.Pool.Count; i++)
				if (GodotObject.IsInstanceValid(state.Pool[i])) state.Pool[i].Visible = false;
		}

		foreach (var kv in _states)
		{
			if (client.ServerHitboxTransforms.ContainsKey(kv.Key)) continue;
			foreach (var m in kv.Value.Pool)
				if (GodotObject.IsInstanceValid(m)) m.Visible = false;
		}
	}

	/// <summary>Builds the mesh for the hitbox index from the puppet's HitboxRig collision shape.</summary>
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
