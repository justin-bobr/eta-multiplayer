using Godot;

namespace Vantix.Fx;

/// <summary>
/// A fixed-length cylindrical streak that travels at bullet speed from muzzle to impact.
/// Spawn with <see cref="Spawn"/>; auto-frees once its front passes the endpoint.
/// </summary>
public partial class BulletTracer : Node3D
{
	private MeshInstance3D _mesh;
	private StandardMaterial3D _material;
	private Vector3 _origin;
	private Vector3 _direction;
	private float _totalDistance;
	private float _speed;
	private float _streakLength;
	private float _age;
	private Color _startColor;

	/// <summary>Creates a tracer under the scene root, initializes it and returns the instance.</summary>
	public static BulletTracer Spawn(SceneTree tree, Vector3 origin, Vector3 endpoint, Color color, float width, float speed, float streakLength)
	{
		var tracer = new BulletTracer();
		tree.Root.AddChild(tracer);
		tracer.Initialize(origin, endpoint, color, width, speed, streakLength);
		return tracer;
	}

	/// <summary>Builds the cylinder mesh/material and places the streak just behind the origin.</summary>
	public void Initialize(Vector3 origin, Vector3 endpoint, Color color, float width, float speed, float streakLength)
	{
		_startColor = color;
		_origin = origin;
		_speed = Mathf.Max(50f, speed);
		_streakLength = Mathf.Max(0.1f, streakLength);

		Vector3 delta = endpoint - origin;
		_totalDistance = delta.Length();
		if (_totalDistance < 0.01f) { QueueFree(); return; }
		_direction = delta / _totalDistance;

		_mesh = new MeshInstance3D
		{
			Mesh = new CylinderMesh
			{
				TopRadius = width,
				BottomRadius = width,
				Height = _streakLength,
				RadialSegments = 6,
				Rings = 0,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_material = new StandardMaterial3D
		{
			AlbedoColor = color,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			DisableReceiveShadows = true,
		};
		_mesh.MaterialOverride = _material;
		AddChild(_mesh);

		Vector3 refUp = Mathf.Abs(_direction.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
		Vector3 xAxis = _direction.Cross(refUp).Normalized();
		Vector3 zAxis = xAxis.Cross(_direction).Normalized();
		var basis = new Basis(xAxis, _direction, zAxis);

		Vector3 frontPos = origin;
		Vector3 midpoint = frontPos - _direction * (_streakLength * 0.5f);
		GlobalTransform = new Transform3D(basis, midpoint);
	}

	/// <summary>Advances the streak each frame and frees the node once its front has passed the endpoint.</summary>
	public override void _Process(double delta)
	{
		using var _prof = MiniProfiler.SampleClient("BulletTracer._Process");
		_age += (float)delta;
		float frontDist = _age * _speed;

		if (frontDist - _streakLength >= _totalDistance)
		{
			QueueFree();
			return;
		}

		float clampedFront = Mathf.Min(frontDist, _totalDistance);
		Vector3 frontPos = _origin + _direction * clampedFront;
		Vector3 midpoint = frontPos - _direction * (_streakLength * 0.5f);

		Transform3D tf = GlobalTransform;
		tf.Origin = midpoint;
		GlobalTransform = tf;

		float totalLife = (_totalDistance + _streakLength) / _speed;
		float t = Mathf.Clamp(_age / totalLife, 0f, 1f);
		float alpha = (1f - t) * _startColor.A;
		_material.AlbedoColor = new Color(_startColor.R, _startColor.G, _startColor.B, alpha);
	}
}
