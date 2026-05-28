using Godot;

/// <summary>
/// Decal texture bundle for one material type. Storable as a .tres resource.
/// Holds the Godot decal channels (Albedo, Normal, ORM, Emission). The ORM slot may either
/// be pre-packed or auto-packed on first access from the separate Occlusion/Roughness/Metallic
/// inputs; the packed result is cached. Height/displacement is not supported by Godot decals.
/// </summary>
[GlobalClass]
public partial class BulletDecalSet : Resource
{
	[ExportGroup("Core Decal Maps")]
	[Export] public Texture2D Albedo;
	[Export] public Texture2D Normal;
	[Export] public Texture2D Emission;
	/// <summary>Optional separate alpha mask, merged into Albedo.A when Albedo has no alpha.</summary>
	[Export] public Texture2D Opacity;

	[ExportGroup("PBR Channels (auto-packed into ORM for Godot Decal)")]
	[Export] public Texture2D Occlusion;
	[Export] public Texture2D Roughness;
	[Export] public Texture2D Metallic;

	[ExportGroup("Tuning")]
	[Export] public Color Modulate = Colors.White;
	[Export(PropertyHint.Range, "0,1,0.05")] public float AlbedoMix = 1.0f;
	[Export(PropertyHint.Range, "0,2,0.05")] public float NormalFade = 0.0f;
	/// <summary>
	/// Resolution cap for the generated textures (ORM + Albedo-with-Opacity). 4K source textures
	/// for a ~18cm decal are wasteful in pack time and VRAM; 1024 is already generous.
	/// </summary>
	[Export(PropertyHint.Range, "128,4096,128")] public int MaxPackResolution = 1024;

	private Texture2D _packedOrm;
	private bool _ormPackTried;
	private Texture2D _packedAlbedo;
	private bool _albedoPackTried;
	private readonly object _packLock = new();

	/// <summary>Returns the auto-packed ORM texture from separate O/R/M channels. Cached and thread-safe.</summary>
	public Texture2D GetEffectiveOrm()
	{
		lock (_packLock)
		{
			if (_ormPackTried) return _packedOrm;
			_packedOrm = PackOrm();
			_ormPackTried = true;
			return _packedOrm;
		}
	}

	/// <summary>Returns Albedo with merged Opacity if the Opacity slot is set, else the raw Albedo. Thread-safe.</summary>
	public Texture2D GetEffectiveAlbedo()
	{
		if (Albedo == null || Opacity == null) return Albedo;
		lock (_packLock)
		{
			if (_albedoPackTried) return _packedAlbedo ?? Albedo;
			_packedAlbedo = MergeAlbedoOpacity();
			_albedoPackTried = true;
			return _packedAlbedo ?? Albedo;
		}
	}

	/// <summary>Merges the Opacity texture's red channel into the Albedo alpha channel.</summary>
	private Texture2D MergeAlbedoOpacity()
	{
		int w = Albedo.GetWidth(), h = Albedo.GetHeight();
		if (w <= 0 || h <= 0) return null;
		ClampSize(ref w, ref h);

		byte[] albedo = NormalizedBytes(Albedo, w, h, Image.Format.Rgba8);
		byte[] opacity = NormalizedBytes(Opacity, w, h, Image.Format.Rgb8);
		if (albedo == null || opacity == null) return null;

		int count = w * h;
		for (int i = 0; i < count; i++)
			albedo[i * 4 + 3] = opacity[i * 3];

		var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, albedo);
		img.GenerateMipmaps();
		Dbg.Print($"[BulletDecalSet] merged Albedo+Opacity ({w}x{h})");
		return ImageTexture.CreateFromImage(img);
	}

	/// <summary>Packs separate O/R/M textures into a single RGB image for the Godot decal ORM slot.</summary>
	private Texture2D PackOrm()
	{
		if (Occlusion == null && Roughness == null && Metallic == null) return null;
		Texture2D refTex = Occlusion ?? Roughness ?? Metallic;
		int w = refTex.GetWidth(), h = refTex.GetHeight();
		if (w <= 0 || h <= 0) return null;
		ClampSize(ref w, ref h);

		byte[] o = Occlusion != null ? NormalizedBytes(Occlusion, w, h, Image.Format.Rgb8) : null;
		byte[] r = Roughness != null ? NormalizedBytes(Roughness, w, h, Image.Format.Rgb8) : null;
		byte[] m = Metallic != null ? NormalizedBytes(Metallic, w, h, Image.Format.Rgb8) : null;

		int count = w * h;
		var packed = new byte[count * 3];
		for (int i = 0; i < count; i++)
		{
			packed[i * 3 + 0] = o != null ? o[i * 3] : (byte)255;
			packed[i * 3 + 1] = r != null ? r[i * 3] : (byte)128;
			packed[i * 3 + 2] = m != null ? m[i * 3] : (byte)0;
		}
		var img = Image.CreateFromData(w, h, false, Image.Format.Rgb8, packed);
		img.GenerateMipmaps();
		Dbg.Print($"[BulletDecalSet] auto-packed ORM ({w}x{h})");
		return ImageTexture.CreateFromImage(img);
	}

	/// <summary>Scales (w,h) down to <see cref="MaxPackResolution"/>, preserving the aspect ratio.</summary>
	private void ClampSize(ref int w, ref int h)
	{
		int max = Mathf.Max(128, MaxPackResolution);
		if (w <= max && h <= max) return;
		float s = (float)max / Mathf.Max(w, h);
		w = Mathf.Max(1, Mathf.RoundToInt(w * s));
		h = Mathf.Max(1, Mathf.RoundToInt(h * s));
	}

	/// <summary>
	/// Returns the raw pixel bytes of a texture in the requested format and size. Decompresses,
	/// converts and resizes with Lanczos as needed (good downscale quality for large source maps).
	/// </summary>
	private static byte[] NormalizedBytes(Texture2D tex, int w, int h, Image.Format fmt)
	{
		Image img = tex.GetImage();
		if (img == null) return null;
		if (img.IsCompressed() && img.Decompress() != Error.Ok) return null;
		if (img.GetFormat() != fmt) img.Convert(fmt);
		if (img.GetWidth() != w || img.GetHeight() != h)
			img.Resize(w, h, Image.Interpolation.Lanczos);
		return img.GetData();
	}
}
