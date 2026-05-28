using Godot;
using Godot.Collections;

namespace Game.Fx;

/// <summary>
/// Converts a 2D horizontal LUT strip into an ImageTexture3D and assigns it
/// to the parent WorldEnvironment's color-correction slot.
/// </summary>
[Tool]
public partial class LutTexture3D : Node
{
	[Export] public Texture2D LutStrip;
	[Export] public int LutSize = 16;

	/// <summary>Builds the 3D LUT on scene load and assigns it to the parent WorldEnvironment.</summary>
	public override void _Ready()
	{
		if (LutStrip == null) return;
		var parent = GetParent<WorldEnvironment>();
		if (parent?.Environment == null) return;
		parent.Environment.AdjustmentColorCorrection = BuildTexture3D(LutStrip, LutSize);
	}

	/// <summary>Builds an ImageTexture3D by slicing a horizontal LUT strip into N square slices.</summary>
	public static ImageTexture3D BuildTexture3D(Texture2D strip, int size)
	{
		var src = strip.GetImage();
		if (src.IsCompressed())
			src.Decompress();
		if (src.GetWidth() != size * size || src.GetHeight() != size)
		{
			GD.PushError($"LutTexture3D: expected {size * size}x{size} strip, got {src.GetWidth()}x{src.GetHeight()}");
			return null;
		}

		var slices = new Array<Image>();
		for (int z = 0; z < size; z++)
		{
			var slice = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
			for (int y = 0; y < size; y++)
			for (int x = 0; x < size; x++)
				slice.SetPixel(x, y, src.GetPixel(z * size + x, y));
			slices.Add(slice);
		}

		var tex = new ImageTexture3D();
		tex.Create(Image.Format.Rgb8, size, size, size, false, slices);
		return tex;
	}
}
