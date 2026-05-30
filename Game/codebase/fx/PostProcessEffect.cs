using Godot;

/// <summary>
/// CompositorEffect running after all transparency. Combines all screen-space
/// post effects (chromatic aberration, sharpening, vignette, film grain,
/// motion blur) into a single compute pass. Hooked into the render pipeline
/// via a Compositor on the WorldEnvironment (compositor.tres). Requires a
/// non-multisampled color buffer (TAA, not MSAA). Each effect can be toggled
/// individually.
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
	private readonly StringName _context = "PostProcessFX";
	private readonly StringName _tempName = "temp_color";
	// Release-build diagnostic: wir hatten den Fall dass im exportierten Build alles zu hell aussah,
	// weil der CompositorEffect (Vignette/CA/Sharpen) silent failed — Dbg.Print ist hinter
	// global/debug gated, also war kein Log sichtbar. Diese Flags treiben ungated GD.Print/PrintErr
	// für den vollen Lifecycle (Constructor → Init → erster RenderCallback). One-shot via Interlocked
	// damit Multi-View / Multi-Frame nicht den Log floodet.
	private int _firstRenderLogged;

	/// <summary>Configures the effect callback slot, requests required render targets, and queues compute init.</summary>
	public PostProcessEffect()
	{
		EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
		AccessResolvedColor = true;
		AccessResolvedDepth = true;
		NeedsMotionVectors = true;
		if (!Engine.IsEditorHint())
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
	}

	/// <summary>Frees GPU resources (shader, pipeline, samplers) when the effect is being destroyed.
	/// _pipeline was missing here — caused a "1 RID of type Compute was leaked" warning on shutdown.</summary>
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

	/// <summary>Per-view render entry point: copies scene colour to a temp buffer then runs the effects pass back to colour.</summary>
	public override void _RenderCallback(int effectCallbackType, RenderData renderData)
	{
		if (_rd == null || !_pipeline.IsValid)
		{
			if (System.Threading.Interlocked.Exchange(ref _firstRenderLogged, 1) == 0)
				GD.PrintErr($"[PostProcessFX] _RenderCallback early-return: rd={(_rd != null)} pipeline.valid={_pipeline.IsValid} — post-pass is silently OFF this run");
			return;
		}
		if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers)
			return;
		if (renderData.GetRenderSceneData() is not RenderSceneDataRD sceneData)
			return;
		if (System.Threading.Interlocked.Exchange(ref _firstRenderLogged, 1) == 0)
			GD.Print("[PostProcessFX] _RenderCallback first dispatch — post-pass IS running");

		Vector2I size = buffers.GetInternalSize();
		if (size.X == 0 || size.Y == 0)
			return;

		uint xGroups = ((uint)size.X - 1) / 8 + 1;
		uint yGroups = ((uint)size.Y - 1) / 8 + 1;
		float time = (Time.GetTicksMsec() % 100000UL) / 1000.0f;
		uint views = buffers.GetViewCount();
		Vector3 camPos = sceneData.GetCamTransform().Origin;

		for (uint view = 0; view < views; view++)
		{
			Rid color = buffers.GetColorLayer(view);

			if ((_rd.TextureGetFormat(color).UsageBits
				& RenderingDevice.TextureUsageBits.StorageBit) == 0)
				return;

			Rid depth = buffers.GetDepthLayer(view);

			Rid velocity = buffers.GetVelocityLayer(view);
			bool hasVelocity = velocity.IsValid;
			if (!hasVelocity)
				velocity = depth;
			float motionBlur = (MotionBlur && hasVelocity) ? MotionBlurStrength : 0.0f;

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

			RunPass(color, temp, depth, velocity, invViewProj, camPos, size, xGroups, yGroups, time, 0.0f, 0.0f);
			RunPass(temp, color, depth, velocity, invViewProj, camPos, size, xGroups, yGroups, time, 1.0f + GrainMode, motionBlur);
		}
	}

	/// <summary>Dispatches a single compute pass with the given source/dest bindings and push constants.</summary>
	private void RunPass(Rid src, Rid dst, Rid depth, Rid velocity, Projection invViewProj, Vector3 camPos,
		Vector2I size, uint xGroups, uint yGroups, float time, float mode, float motionBlur)
	{
		float[] push = new float[32];
		push[0] = invViewProj.X.X; push[1] = invViewProj.X.Y; push[2] = invViewProj.X.Z; push[3] = invViewProj.X.W;
		push[4] = invViewProj.Y.X; push[5] = invViewProj.Y.Y; push[6] = invViewProj.Y.Z; push[7] = invViewProj.Y.W;
		push[8] = invViewProj.Z.X; push[9] = invViewProj.Z.Y; push[10] = invViewProj.Z.Z; push[11] = invViewProj.Z.W;
		push[12] = invViewProj.W.X; push[13] = invViewProj.W.Y; push[14] = invViewProj.W.Z; push[15] = invViewProj.W.W;
		push[16] = camPos.X; push[17] = camPos.Y; push[18] = camPos.Z;
		push[19] = motionBlur;
		push[20] = size.X; push[21] = size.Y;
		push[22] = ChromaticAberration ? Aberration : 0.0f;
		push[23] = Sharpening ? Sharpen : 0.0f;
		push[24] = FilmGrain ? GrainStrength : 0.0f;
		push[25] = time; push[26] = mode;
		push[27] = Vignette ? (VignetteStrength + VignetteAdsBoost * AdsBlend) : 0.0f;
		push[28] = VignetteRadius;

		byte[] pushBytes = new byte[push.Length * sizeof(float)];
		System.Buffer.BlockCopy(push, 0, pushBytes, 0, pushBytes.Length);

		var srcUniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.SamplerWithTexture,
			Binding = 0,
		};
		srcUniform.AddId(_linearSampler);
		srcUniform.AddId(src);
		var dstUniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.Image,
			Binding = 1,
		};
		dstUniform.AddId(dst);
		var depthUniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.SamplerWithTexture,
			Binding = 2,
		};
		depthUniform.AddId(_sampler);
		depthUniform.AddId(depth);
		var velocityUniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.SamplerWithTexture,
			Binding = 3,
		};
		velocityUniform.AddId(_sampler);
		velocityUniform.AddId(velocity);

		Rid uniformSet = UniformSetCacheRD.GetCache(_shader, 0,
			new Godot.Collections.Array<RDUniform> { srcUniform, dstUniform, depthUniform, velocityUniform });
		if (!uniformSet.IsValid)
			return;

		long list = _rd.ComputeListBegin();
		_rd.ComputeListBindComputePipeline(list, _pipeline);
		_rd.ComputeListBindUniformSet(list, uniformSet, 0);
		_rd.ComputeListSetPushConstant(list, pushBytes, (uint)pushBytes.Length);
		_rd.ComputeListDispatch(list, xGroups, yGroups, 1);
		_rd.ComputeListEnd();
	}
}
