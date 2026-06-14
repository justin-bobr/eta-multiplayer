using Godot;

/// <summary>Serialisable Fog-of-War PVS data — output of <see cref="VoxelPvsInstance"/>'s editor bake.
/// Ships as a .tres next to the map .tscn; <see cref="NetServer"/> loads it via <see cref="VoxelPvs.LoadFromData"/>
/// for FoW from tick 1 with no runtime build.
/// Layout: flat <see cref="VisibilityBytes"/> packing N² bits (N = <see cref="TotalVoxels"/>); bit (a×N + b) = 1
/// means voxel a sees b. Symmetric (both (a,b) and (b,a) set), so query order is irrelevant.</summary>
[Tool]
[GlobalClass]
public partial class VoxelPvsData : Resource
{
	/// <summary>World-space min corner of the voxel grid AABB. World position <c>p</c> maps to voxel
	/// index <c>floor((p - Origin) / VoxelSize)</c>.</summary>
	[Export] public Vector3 Origin { get; set; }
	/// <summary>Edge length of one cubic voxel cell in metres. Matches the value used at bake time
	/// (possibly auto-coarsened from the requested size to fit the voxel budget).</summary>
	[Export] public float VoxelSize { get; set; } = 4.0f;
	/// <summary>Number of cells per axis. Total voxel count = X × Y × Z.</summary>
	[Export] public Vector3I Dims { get; set; }
	/// <summary>Packed visibility bits. Length = ceil(N² / 8). Bit i = byte[i>>3] & (1 << (i & 7)).</summary>
	[Export] public byte[] VisibilityBytes { get; set; }

	public int TotalVoxels => Dims.X * Dims.Y * Dims.Z;
	public bool HasData => VisibilityBytes != null && VisibilityBytes.Length > 0 && TotalVoxels > 0;

	/// <summary>Per voxel, the count of voxels it can see (incl. itself). O(N²) bit-scan (~1s at N=16k) —
	/// cache if queried repeatedly. Feeds the density-heatmap gizmo.</summary>
	public int[] ComputePerVoxelVisibleCounts()
	{
		if (!HasData) return System.Array.Empty<int>();
		int n = TotalVoxels;
		var counts = new int[n];
		for (int a = 0; a < n; a++)
		{
			long rowStart = (long)a * n;
			int count = 0;
			for (int b = 0; b < n; b++)
			{
				long bit = rowStart + b;
				if ((VisibilityBytes[bit >> 3] & (1 << (int)(bit & 7))) != 0) count++;
			}
			counts[a] = count;
		}
		return counts;
	}
}
