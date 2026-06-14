namespace Vantix.Utils;

#if TOOLS
using Godot;

/// <summary>Shared box-wireframe helper for the gizmo plugins. Returns the 24 endpoints of the 12
/// edges of an AABB centred at origin, laid out as line pairs for
/// <see cref="EditorNode3DGizmo.AddLines"/>. Centralised so all gizmos draw identical outlines.</summary>
internal static class GizmoBoxBuilder
{
	public static Vector3[] BuildLines(Vector3 size)
	{
		Vector3 h = size * 0.5f;
		Vector3 c0 = new(-h.X, -h.Y, -h.Z);
		Vector3 c1 = new(+h.X, -h.Y, -h.Z);
		Vector3 c2 = new(+h.X, +h.Y, -h.Z);
		Vector3 c3 = new(-h.X, +h.Y, -h.Z);
		Vector3 c4 = new(-h.X, -h.Y, +h.Z);
		Vector3 c5 = new(+h.X, -h.Y, +h.Z);
		Vector3 c6 = new(+h.X, +h.Y, +h.Z);
		Vector3 c7 = new(-h.X, +h.Y, +h.Z);
		return new[]
		{
			// bottom rectangle
			c0, c1, c1, c2, c2, c3, c3, c0,
			// top rectangle
			c4, c5, c5, c6, c6, c7, c7, c4,
			// verticals
			c0, c4, c1, c5, c2, c6, c3, c7,
		};
	}

	/// <summary>Solid <see cref="BoxMesh"/> for the gizmo's transparent fill body. Matches
	/// <see cref="BuildLines"/> at the same size.</summary>
	public static BoxMesh BuildBoxMesh(Vector3 size) => new() { Size = size };
}
#endif
