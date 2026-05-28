using Godot;

/// <summary>
/// Serialisable Fog-of-War PVS data — the output of <see cref="VoxelPvsInstance"/>'s editor bake.
/// Ships as a .tres resource next to the map .tscn (e.g. <c>de_dust2.pvs.tres</c>) and gets loaded
/// at server start via the <see cref="VoxelPvsInstance.Data"/> reference. Avoids the runtime build
/// cost entirely once the map has been baked: <see cref="NetServer"/> calls <see cref="VoxelPvs.LoadFromData"/>
/// and FoW is active from tick 1, no first-start freeze.
///
/// Layout: a flat <see cref="VisibilityBytes"/> byte array packing N² bits (N = <see cref="TotalVoxels"/>).
/// Bit (a × N + b) = 1 means voxel a can see voxel b. Symmetric — both (a, b) and (b, a) bits are set
/// at bake time so query order does not matter.
/// </summary>
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

	/// <summary>For each voxel index, returns the count of OTHER voxels it can see (incl. itself).
	/// O(N²) bit-scan — at N=16k takes ~1 second one-shot, so cache the result if you query repeatedly.
	/// Used by <see cref="VoxelPvsInstance"/>'s density-heatmap gizmo to color cells by how "open" they
	/// are: high count = open area, low count = enclosed.</summary>
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
