using Godot;

/// <summary>
/// Post-transparency CompositorEffect combining chromatic aberration, sharpening, vignette, film grain
/// and motion blur into one compute pass. Hooked via the WorldEnvironment compositor (compositor.tres).
/// Requires a non-multisampled color buffer (TAA, not MSAA).
/// </summary>
[Tool]
[GlobalClass]
public partial class PostProcessEffect : CompositorEffect
{
	[Export] public bool ChromaticAberration = true;
	[Export] public bool Sharpening = true;
	[Export] public bool Vignette = true;
	[Export] public bool FilmGrain = true;
	[Export] public bool MotionBlur = true;
	[Export(PropertyHint.Enum, "Simple,FilmGrain,FilmGrain2")] public int GrainMode = 1;

	[Export(PropertyHint.Range, "0.0,0.02,0.0001")] public float Aberration = 0.0026f;
	[Export(PropertyHint.Range, "0.0,2.0,0.01")] public float Sharpen = 0.25f;
	[Export(PropertyHint.Range, "0.0,1.0,0.01")] public float VignetteStrength = 0.18f;
	[Export(PropertyHint.Range, "0.2,1.5,0.01")] public float VignetteRadius = 1.05f;
	[Export(PropertyHint.Range, "0.0,0.5,0.01")] public float VignetteAdsBoost = 0.15f;
	/// <summary>Runtime value driven by LocalAnimation: 0 = no ADS, 1 = full ADS boost.</summary>
	public float AdsBlend = 0f;
	[Export(PropertyHint.Range, "0.0,0.5,0.005")] public float GrainStrength = 0.085f;
	[Export(PropertyHint.Range, "0.0,8.0,0.1")] public float MotionBlurStrength = 3.0f;

	private RenderingDevice _rd;
	private Rid _shader;
	private Rid _pipeline;
	private Rid _sampler;
	private Rid _linearSampler;

	private RDUniform _srcUniform;
	private RDUniform _dstUniform;
	private RDUniform _depthUniform;
	private RDUniform _velocityUniform;
	private Godot.Collections.Array<RDUniform> _uniformList;
	private readonly float[] _pushFloats = new float[32];
	private readonly byte[] _pushBytes1 = new byte[32 * sizeof(float)];
	private readonly byte[] _pushBytes2 = new byte[32 * sizeof(float)];

	private readonly System.Collections.Generic.Dictionary<ulong, bool> _storageCheckCache
		= new System.Collections.Generic.Dictionary<ulong, bool>(4);

	private readonly System.Collections.Generic.Dictionary<(ulong src, ulong dst), Rid> _setCache
		= new System.Collections.Generic.Dictionary<(ulong, ulong), Rid>(4);
	private readonly StringName _context = "PostProcessFX";
	private readonly StringName _tempName = "temp_color";
	private int _firstRenderLogged;
	private int _mbDiagLogged;


	/// <summary>True under the dummy renderer (--headless / dedicated server) where no RenderingDevice exists.</summary>
	private static bool IsHeadless() =>
		OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless";

	/// <summary>Configures the effect callback slot, requests required render targets, and queues compute init.</summary>
	public PostProcessEffect()
	{
		EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
		AccessResolvedColor = true;
		AccessResolvedDepth = true;
		NeedsMotionVectors = true;
		if (!Engine.IsEditorHint() && !IsHeadless())
		{
			GD.Print("[PostProcessFX] ctor — queueing InitializeCompute on render thread");
			RenderingServer.CallOnRenderThread(Callable.From(InitializeCompute));
		}
	}
	/// <summary>Loads the compute shader and creates the pipeline plus samplers on the render thread.</summary>
	private void InitializeCompute()
	{
		_rd = RenderingServer.GetRenderingDevice();
		if (_rd == null)
		{
			GD.PrintErr("[PostProcessFX] InitializeCompute: RenderingServer.GetRenderingDevice() returned null — local RD unavailable, post-pass will NOT run");
			return;
		}
		var shaderFile = GD.Load<RDShaderFile>("res://maps/dust/post_process.glsl");
		if (shaderFile == null)
		{
			GD.PrintErr("[PostProcessFX] post_process.glsl could not be loaded — file likely missing from export. Check export_presets.cfg include filters for *.glsl");
			return;
		}
		var spirv = shaderFile.GetSpirV();
		string compileErr = spirv.GetStageCompileError(RenderingDevice.ShaderStage.Compute);
		if (!string.IsNullOrEmpty(compileErr))
			GD.PrintErr($"[PostProcessFX] SHADER COMPILE ERROR:\n{compileErr}");
		_shader = _rd.ShaderCreateFromSpirV(spirv);
		if (_shader.IsValid)
		{
			_pipeline = _rd.ComputePipelineCreate(_shader);
			GD.Print($"[PostProcessFX] Shader + Pipeline OK (pipeline.valid={_pipeline.IsValid})");
		}
		else
		{
			GD.PrintErr("[PostProcessFX] Shader RID invalid — pipeline not created, post-pass will be a no-op");
		}
		_sampler = _rd.SamplerCreate(new RDSamplerState());
		_linearSampler = _rd.SamplerCreate(new RDSamplerState
		{
			MinFilter = RenderingDevice.SamplerFilter.Linear,
			MagFilter = RenderingDevice.SamplerFilter.Linear,
			RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
			RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
		});

		_srcUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 };
		_dstUniform = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
		_depthUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 2 };
		_velocityUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 3 };
		_uniformList = new Godot.Collections.Array<RDUniform> { _srcUniform, _dstUniform, _depthUniform, _velocityUniform };
	}


	/// <summary>Frees GPU resources (shader, pipeline, samplers) on destruction.</summary>
	public override void _Notification(int what)
	{
		if (what == NotificationPredelete && _rd != null)
		{
			if (_pipeline.IsValid)
				_rd.FreeRid(_pipeline);
			if (_shader.IsValid)
				_rd.FreeRid(_shader);
			if (_sampler.IsValid)
				_rd.FreeRid(_sampler);
			if (_linearSampler.IsValid)
				_rd.FreeRid(_linearSampler);
		}
	}

	/// <summary>True when at least one sub-effect contributes; lets <see cref="_RenderCallback"/> skip the whole dispatch when all are off.</summary>
	private bool AnyEffectActive =>
		(ChromaticAberration && Aberration > 0f) ||
		(Sharpening && Sharpen > 0f) ||
		(FilmGrain && GrainStrength > 0f) ||
		(Vignette && (VignetteStrength + VignetteAdsBoost * AdsBlend) > 0f) ||
		(MotionBlur && MotionBlurStrength > 0f);

	/// <summary>Per-view render entry point: copies scene colour to a temp buffer then runs the effects pass back to colour.</summary>
	public override void _RenderCallback(int effectCallbackType, RenderData renderData)
	{
		if (_rd == null || !_pipeline.IsValid)
		{
			if (System.Threading.Interlocked.Exchange(ref _firstRenderLogged, 1) == 0)
				GD.PrintErr($"[PostProcessFX] _RenderCallback early-return: rd={(_rd != null)} pipeline.valid={_pipeline.IsValid} — post-pass is silently OFF this run");
			return;
		}
		if (!AnyEffectActive)
			return;
		if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers)
			return;
		if (renderData.GetRenderSceneData() is not RenderSceneDataRD sceneData)
			return;
		if (System.Threading.Interlocked.Exchange(ref _firstRenderLogged, 1) == 0)
			GD.Print("[PostProcessFX] _RenderCallback first dispatch — post-pass IS running");

		Vector2I size = buffers.GetInternalSize();
		if (size.X == 0 || size.Y == 0)
			return;

		uint xGroups = ((uint)size.X + 15) / 16;
		uint yGroups = ((uint)size.Y + 15) / 16;
		float time = (Time.GetTicksMsec() % 100000UL) / 1000.0f;
		uint views = buffers.GetViewCount();
		Vector3 camPos = sceneData.GetCamTransform().Origin;

		for (uint view = 0; view < views; view++)
		{
			Rid color = buffers.GetColorLayer(view);

			if (!_storageCheckCache.TryGetValue(color.Id, out bool isStorage))
			{
				isStorage = (_rd.TextureGetFormat(color).UsageBits
					& RenderingDevice.TextureUsageBits.StorageBit) != 0;
				_storageCheckCache[color.Id] = isStorage;
			}
			if (!isStorage)
				return;

			Rid depth = buffers.GetDepthLayer(view);

			Rid velocity = buffers.GetVelocityLayer(view);
			bool hasVelocity = velocity.IsValid;
			if (!hasVelocity)
				velocity = depth;
			float motionBlur = (MotionBlur && hasVelocity) ? MotionBlurStrength : 0.0f;
			if (System.Threading.Interlocked.Exchange(ref _mbDiagLogged, 1) == 0)
				GD.Print($"[PostProcessFX] MB diag: MotionBlur={MotionBlur} hasVelocity={hasVelocity} strength={MotionBlurStrength} => effectiveMB={motionBlur} (no velocity layer = no TAA/FSR2 motion vectors → MB stays 0)");

			if (!buffers.HasTexture(_context, _tempName))
			{
				buffers.CreateTexture(_context, _tempName,
					RenderingDevice.DataFormat.R16G16B16A16Sfloat,
					(uint)(RenderingDevice.TextureUsageBits.StorageBit
						| RenderingDevice.TextureUsageBits.SamplingBit),
					RenderingDevice.TextureSamples.Samples1,
					size, views, 1, true, false);
			}
			Rid temp = buffers.GetTextureSlice(_context, _tempName, view, 0, 1, 1);
			Projection viewMatrix = new Projection(sceneData.GetCamTransform().AffineInverse());
			Projection invViewProj = (sceneData.GetViewProjection(view) * viewMatrix).Inverse();

			RunBothPasses(color, temp, depth, velocity, invViewProj, camPos, size, xGroups, yGroups, time, motionBlur);
		}
	}

	/// <summary>Dispatches both passes (mode 0 colour->temp, mode 1 temp->colour) in one ComputeListBegin/End with a barrier
	/// between. Each pass uses its own pre-allocated push-constant byte buffer so the command list captures the right snapshot.</summary>
	private void RunBothPasses(Rid color, Rid temp, Rid depth, Rid velocity, Projection invViewProj,
		Vector3 camPos, Vector2I size, uint xGroups, uint yGroups, float time, float motionBlur)
	{
		_pushFloats[0] = invViewProj.X.X;
		_pushFloats[1] = invViewProj.X.Y;
		_pushFloats[2] = invViewProj.X.Z;
		_pushFloats[3] = invViewProj.X.W;
		_pushFloats[4] = invViewProj.Y.X;
		_pushFloats[5] = invViewProj.Y.Y;
		_pushFloats[6] = invViewProj.Y.Z;
		_pushFloats[7] = invViewProj.Y.W;
		_pushFloats[8] = invViewProj.Z.X;
		_pushFloats[9] = invViewProj.Z.Y;
		_pushFloats[10] = invViewProj.Z.Z;
		_pushFloats[11] = invViewProj.Z.W;
		_pushFloats[12] = invViewProj.W.X;
		_pushFloats[13] = invViewProj.W.Y;
		_pushFloats[14] = invViewProj.W.Z;
		_pushFloats[15] = invViewProj.W.W;
		_pushFloats[16] = camPos.X;
		_pushFloats[17] = camPos.Y;
		_pushFloats[18] = camPos.Z;
		_pushFloats[20] = size.X;
		_pushFloats[21] = size.Y;
		_pushFloats[22] = ChromaticAberration ? Aberration : 0.0f;
		_pushFloats[23] = Sharpening ? Sharpen : 0.0f;
		_pushFloats[24] = FilmGrain ? GrainStrength : 0.0f;
		_pushFloats[25] = time;
		_pushFloats[27] = Vignette ? (VignetteStrength + VignetteAdsBoost * AdsBlend) : 0.0f;
		_pushFloats[28] = VignetteRadius;

		_pushFloats[19] = 0.0f;
		_pushFloats[26] = 0.0f;
		System.Buffer.BlockCopy(_pushFloats, 0, _pushBytes1, 0, _pushBytes1.Length);

		Rid set1 = GetOrCreateSet(color, temp, depth, velocity);
		if (!set1.IsValid)
			return;

		_pushFloats[19] = motionBlur;
		_pushFloats[26] = 1.0f + GrainMode;
		System.Buffer.BlockCopy(_pushFloats, 0, _pushBytes2, 0, _pushBytes2.Length);

		Rid set2 = GetOrCreateSet(temp, color, depth, velocity);
		if (!set2.IsValid)
			return;

		long list = _rd.ComputeListBegin();
		_rd.ComputeListBindComputePipeline(list, _pipeline);

		_rd.ComputeListBindUniformSet(list, set1, 0);
		_rd.ComputeListSetPushConstant(list, _pushBytes1, (uint)_pushBytes1.Length);
		_rd.ComputeListDispatch(list, xGroups, yGroups, 1);

		_rd.ComputeListAddBarrier(list);

		_rd.ComputeListBindUniformSet(list, set2, 0);
		_rd.ComputeListSetPushConstant(list, _pushBytes2, (uint)_pushBytes2.Length);
		_rd.ComputeListDispatch(list, xGroups, yGroups, 1);

		_rd.ComputeListEnd();
	}

	/// <summary>Returns the cached UniformSet for the (src, dst) pair, creating one on first use. Stale entries (UniformSetIsValid) are dropped and rebuilt.</summary>
	private Rid GetOrCreateSet(Rid src, Rid dst, Rid depth, Rid velocity)
	{
		var key = (src.Id, dst.Id);
		if (_setCache.TryGetValue(key, out var cached))
		{
			if (_rd.UniformSetIsValid(cached))
				return cached;
			_setCache.Remove(key);
		}

		_srcUniform.ClearIds();
		_srcUniform.AddId(_linearSampler);
		_srcUniform.AddId(src);
		_dstUniform.ClearIds();
		_dstUniform.AddId(dst);
		_depthUniform.ClearIds();
		_depthUniform.AddId(_sampler);
		_depthUniform.AddId(depth);
		_velocityUniform.ClearIds();
		_velocityUniform.AddId(_sampler);
		_velocityUniform.AddId(velocity);

		Rid fresh = _rd.UniformSetCreate(_uniformList, _shader, 0);
		_setCache[key] = fresh;
		return fresh;
	}
}
