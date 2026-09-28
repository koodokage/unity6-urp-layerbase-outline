using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class LightweightOutlineFeature : ScriptableRendererFeature
{
    // LAYER PROFILE =============================================================

    [Serializable]
    public class OutlineLayerProfile
    {
        public string layerName;

        [Tooltip("MeshRenderer > Rendering Layer Mask index: 0-31")]
        [Range(0, 31)]
        public int renderingLayer = 0;

        [ColorUsage(true, true)]
        public Color color = Color.white;

        [Tooltip("Bu layer'a ait outline mesafesi, EKRAN (full-res) piksel cinsinden.")]
        [Min(0.5f)]
        public float width = 2.0f;

        [Tooltip(
            "Bu layer'a ait outline'in gorunecegi maksimum kamera mesafesi " +
            "(world units, metre). 0 = sinirsiz mesafe."
        )]
        [Min(0f)]
        public float renderDistance = 0f;

        [Tooltip(
            "Acik: normal derinlik testi; obje baska bir seyin arkasina " +
            "girince outline kaybolur.\n" +
            "Kapali: X-Ray; outline derinlikten bagimsiz her zaman gorunur."
        )]
        public bool depthTest = true;

        [Tooltip(
            "Depth Test acikken kullanilan bias (world units, metre). " +
            "Kendi-kendini-occlude etme titremesini onler."
        )]
        [Min(0f)]
        public float depthBias = 0.05f;
    }

    // SETTINGS =============================================================

    [Serializable]
    public class Settings
    {
        [Header("Rendering Layers")]

        [Tooltip(
            "Outline cizilecek her Rendering Layer icin bir profil ekleyin. " +
            "Ayarlar CPU'da BIR KEZ onbellege alinir (Create / editor'de " +
            "her kare degil). Runtime'da profil degistirirseniz " +
            "RefreshProfiles() cagirin."
        )]
        public List<OutlineLayerProfile> layerProfiles = new();

        [Header("Performance")]

        [Tooltip(
            "Mask / JFA cozunurlugu (kamera hedefine oranla). Dusuk = daha " +
            "hizli, kenarlar daha kaba. Composite tam cozunurlukte, " +
            "sadece outline piksellerine yazar."
        )]
        [Range(0.25f, 1f)]
        public float resolutionScale = 0.5f;

        [Tooltip(
            "JFA sonuna eklenen ekstra step=1 duzeltme pass sayisi. " +
            "0 = en hizli (onerilir). Her ekstra pass ~1 fullscreen pass demek."
        )]
        [Range(0, 2)]
        public int extraRefinementPasses = 0;

        [Header("Render Pass")]

        public RenderPassEvent renderPassEvent =
            RenderPassEvent.BeforeRenderingPostProcessing;
    }

    // FEATURE =============================================================

    public Settings settings = new();

    private Material outlineMaterial;
    private Material maskMaterial;

    private LightweightOutlinePass outlinePass;

    // ---- Onbellege alinmis profil verisi (her karede yeniden hesaplanmaz)
    private readonly Color[] cachedColors = new Color[32];
    private readonly Vector4[] cachedWidths = new Vector4[32];
    private readonly Vector4[] cachedRenderDistance = new Vector4[32];
    private readonly Vector4[] cachedDepthTest = new Vector4[32];
    private uint cachedLayerMask;
    private float cachedMaxWidth;

    private static readonly int LayerColorID = Shader.PropertyToID("_LayerColors");
    private static readonly int LayerWidthID = Shader.PropertyToID("_LayerWidths");
    private static readonly int LayerRenderDistanceID = Shader.PropertyToID("_LayerRenderDistance");
    private static readonly int LayerDepthTestID = Shader.PropertyToID("_LayerDepthTest");

    // CREATE =============================================================

    public override void Create()
    {
        // Editor'de Create tekrar cagrilabilir; eskileri temizle.
        if (outlinePass != null)
        {
            outlinePass.Dispose();
            outlinePass = null;
        }

        CoreUtils.Destroy(outlineMaterial);
        CoreUtils.Destroy(maskMaterial);

        Shader shader = Shader.Find("Hidden/Lightweight Fullscreen Outline");

        if (shader == null)
        {
            Debug.LogError("Lightweight Outline shader not found.");
            return;
        }

        outlineMaterial = CoreUtils.CreateEngineMaterial(shader);
        maskMaterial = CoreUtils.CreateEngineMaterial(shader);

        outlinePass = new LightweightOutlinePass(outlineMaterial, maskMaterial);
        outlinePass.renderPassEvent = settings.renderPassEvent;

        // Composite, per-layer occlusion icin _CameraDepthTexture okuyor.
        outlinePass.ConfigureInput(ScriptableRenderPassInput.Depth);

        RefreshProfiles();
    }

    // =============================================================
    // PROFILE CACHE
    // Profil verisini BIR KEZ hesaplayip material'lere yazar. Runtime'da
    // layerProfiles'i script'ten degistirirseniz bunu cagirin.
    // =============================================================

    public void RefreshProfiles()
    {
        if (outlineMaterial == null || maskMaterial == null)
            return;

        for (int i = 0; i < 32; i++)
        {
            cachedColors[i] = Color.white;
            cachedWidths[i] = new Vector4(2f, 0f, 0f, 0f);
            cachedRenderDistance[i] = Vector4.zero;
            cachedDepthTest[i] = new Vector4(0f, 0.05f, 0f, 0f);
        }

        cachedLayerMask = 0u;
        cachedMaxWidth = 0f;

        List<OutlineLayerProfile> profiles = settings.layerProfiles;

        if (profiles != null)
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                OutlineLayerProfile p = profiles[i];

                int layer = Mathf.Clamp(p.renderingLayer, 0, 31);
                float width = Mathf.Max(0.5f, p.width);

                cachedLayerMask |= (1u << layer);
                cachedMaxWidth = Mathf.Max(cachedMaxWidth, width);

                cachedColors[layer] = p.color;

                // Vector4 array: scalar float array 16 byte hizalama
                // sorununu onler (x = deger).
                cachedWidths[layer] = new Vector4(width, 0f, 0f, 0f);
                cachedRenderDistance[layer] =
                    new Vector4(Mathf.Max(0f, p.renderDistance), 0f, 0f, 0f);

                // x = depth test (1/0), y = bias
                cachedDepthTest[layer] = new Vector4(
                    p.depthTest ? 1f : 0f,
                    Mathf.Max(0f, p.depthBias),
                    0f, 0f);
            }
        }

        outlineMaterial.SetColorArray(LayerColorID, cachedColors);
        outlineMaterial.SetVectorArray(LayerWidthID, cachedWidths);
        outlineMaterial.SetVectorArray(LayerDepthTestID, cachedDepthTest);

        maskMaterial.SetVectorArray(LayerRenderDistanceID, cachedRenderDistance);
    }

   // ADD PASS =============================================================

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        if (outlinePass == null)
            return;

        if (settings.layerProfiles == null || settings.layerProfiles.Count == 0)
            return;

        if (renderingData.cameraData.isPreviewCamera)
            return;

        CameraType cameraType = renderingData.cameraData.cameraType;

        if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
            return;

#if UNITY_EDITOR
        // Editor'de inspector degisiklikleri aninda yansisin.
        // Build'de bu satir derlenmez: sifir CPU maliyeti.
        RefreshProfiles();
#endif

        if (cachedLayerMask == 0u)
            return;

        outlinePass.renderPassEvent = settings.renderPassEvent;

        outlinePass.Setup(
            cachedLayerMask,
            Mathf.Max(1f, cachedMaxWidth),
            Mathf.Clamp(settings.resolutionScale, 0.25f, 1f),
            Mathf.Clamp(settings.extraRefinementPasses, 0, 2)
        );

        renderer.EnqueuePass(outlinePass);
    }

   // DISPOSE =============================================================

    protected override void Dispose(bool disposing)
    {
        if (outlinePass != null)
        {
            outlinePass.Dispose();
            outlinePass = null;
        }

        CoreUtils.Destroy(outlineMaterial);
        CoreUtils.Destroy(maskMaterial);

        outlineMaterial = null;
        maskMaterial = null;
    }
    
    // PASS =============================================================

    private class LightweightOutlinePass : ScriptableRenderPass
    {
        private readonly Material outlineMaterial;
        private readonly Material maskMaterial;

        private uint renderingLayerMask;
        private float maxWidth = 8f;
        private float resolutionScale = 0.5f;
        private int extraRefinementPasses = 0;

        private readonly List<int> cachedSteps = new List<int>(8);

        private static readonly List<ShaderTagId> ShaderTags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        private const int PassMask = 0;
        private const int PassJfaInit = 1;
        private const int PassJfaStep = 2;
        private const int PassComposite = 3;

        private static readonly int OutlineMaskID = Shader.PropertyToID("_OutlineMask");
        private static readonly int JfaSeedID = Shader.PropertyToID("_JFASeedTex");
        private static readonly int JfaStepID = Shader.PropertyToID("_JFAStep");
        private static readonly int JfaReachID = Shader.PropertyToID("_JFAReach");
        private static readonly int MaskTexelID = Shader.PropertyToID("_OutlineMaskTexel");

        private class MaskPassData
        {
            public RendererListHandle rendererList;
        }

        private class JfaBlitPassData
        {
            public TextureHandle source;
            public Material material;
            public int passIndex;
            public float step;
        }

        private class CompositePassData
        {
            public Material material;
        }

        public LightweightOutlinePass(Material outlineMaterial, Material maskMaterial)
        {
            this.outlineMaterial = outlineMaterial;
            this.maskMaterial = maskMaterial;
        }

        public void Setup(
            uint renderingLayerMask,
            float maxWidth,
            float resolutionScale,
            int extraRefinementPasses)
        {
            this.renderingLayerMask = renderingLayerMask;
            this.maxWidth = maxWidth;
            this.resolutionScale = resolutionScale;
            this.extraRefinementPasses = extraRefinementPasses;
        }

        public void Dispose() { }

        // =========================================================
        // JFA STEP SIZES (N, N/2, ..., 1) + extra refinement (step=1)
        // =========================================================

        private static int NextPow2(int v)
        {
            v = Mathf.Max(1, v);
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }

        private List<int> BuildStepSizes(int scaledWidth, int scaledHeight)
        {
            cachedSteps.Clear();

            int radiusTexels = Mathf.CeilToInt(maxWidth * resolutionScale);
            radiusTexels = Mathf.Clamp(radiusTexels, 1, Mathf.Max(scaledWidth, scaledHeight));

            int n = NextPow2(radiusTexels);

            for (int s = n; s >= 1; s >>= 1)
                cachedSteps.Add(s);

            for (int i = 0; i < extraRefinementPasses; i++)
                cachedSteps.Add(1);

            return cachedSteps;
        }

        // RENDER GRAPH =========================================================

        public override void RecordRenderGraph(
            RenderGraph renderGraph,
            ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            // Composite DOGRUDAN kamera rengine blend eder: full-res kopya
            // hedefi yok, sahne rengi okunmuyor.
            TextureHandle colorTarget = resourceData.activeColorTexture;
            TextureHandle cameraDepthTexture = resourceData.cameraDepthTexture;

            RenderTextureDescriptor fullDesc = cameraData.cameraTargetDescriptor;

            int scaledW = Mathf.Max(1, Mathf.RoundToInt(fullDesc.width * resolutionScale));
            int scaledH = Mathf.Max(1, Mathf.RoundToInt(fullDesc.height * resolutionScale));

            // Kamera basina degisen (boyut'a bagli) sabitler.
            outlineMaterial.SetFloat(JfaReachID, (maxWidth + 1f) * resolutionScale);
            outlineMaterial.SetVector(
                MaskTexelID,
                new Vector4(1f / scaledW, 1f / scaledH, scaledW, scaledH));

            // ---- Mask: R = layerID/255, G = device depth
            RenderTextureDescriptor maskDescriptor = fullDesc;
            maskDescriptor.width = scaledW;
            maskDescriptor.height = scaledH;
            maskDescriptor.msaaSamples = 1;
            maskDescriptor.depthBufferBits = 0;
            maskDescriptor.colorFormat = RenderTextureFormat.RGFloat;
            maskDescriptor.sRGB = false;

            TextureHandle maskTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, maskDescriptor, "Lightweight Outline Mask", false);

            RenderTextureDescriptor maskDepthDescriptor = maskDescriptor;
            maskDepthDescriptor.graphicsFormat = GraphicsFormat.None;
            maskDepthDescriptor.depthStencilFormat = GraphicsFormat.D32_SFloat;
            maskDepthDescriptor.depthBufferBits = 32;

            TextureHandle maskDepthTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, maskDepthDescriptor, "Lightweight Outline Mask Depth", false);

            // ---- JFA: RGBAHalf (8 byte/piksel; onceki RGBAFloat'in yarisi).
            // xy = "bosluga bakan" seed, zw = "uzaktaki baska layer'a bakan"
            // seed. Degerler MUTLAK UV degil, kendi pikselinden seed'e TEXEL
            // OFSETI (kucuk tam sayilar => half'ta tam hassasiyet).
            RenderTextureDescriptor jfaDescriptor = fullDesc;
            jfaDescriptor.width = scaledW;
            jfaDescriptor.height = scaledH;
            jfaDescriptor.msaaSamples = 1;
            jfaDescriptor.depthBufferBits = 0;
            jfaDescriptor.colorFormat = RenderTextureFormat.ARGBHalf;
            jfaDescriptor.sRGB = false;

            TextureHandle jfaA = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, jfaDescriptor, "Lightweight Outline JFA A", false);

            TextureHandle jfaB = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, jfaDescriptor, "Lightweight Outline JFA B", false);

            // ---- Mask renderer list (siralama yok, per-object veri yok)
            FilteringSettings filteringSettings = new FilteringSettings(
                RenderQueueRange.all, -1, renderingLayerMask, 0);

            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                ShaderTags, renderingData, cameraData, lightData, SortingCriteria.None);

            drawingSettings.perObjectData = PerObjectData.None;
            drawingSettings.overrideMaterial = maskMaterial;
            drawingSettings.overrideMaterialPassIndex = PassMask;

            RendererListHandle maskRendererList = renderGraph.CreateRendererList(
                new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings));

            // MASK PASS =====================================================

            using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>(
                "Lightweight Outline - Mask", out var passData))
            {
                passData.rendererList = maskRendererList;

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(maskTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(maskDepthTexture, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(maskTexture, OutlineMaskID);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(true, true, Color.black);
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }

            // JFA INIT =====================================================

            using (var builder = renderGraph.AddRasterRenderPass<JfaBlitPassData>(
                "Lightweight Outline - JFA Init", out var passData))
            {
                passData.source = maskTexture;
                passData.material = outlineMaterial;
                passData.passIndex = PassJfaInit;

                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.SetRenderAttachment(jfaA, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (JfaBlitPassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0),
                        data.material, data.passIndex);
                });
            }

            // JFA STEPS =====================================================

            List<int> steps = BuildStepSizes(scaledW, scaledH);

            TextureHandle jfaSrc = jfaA;
            TextureHandle jfaDst = jfaB;

            for (int i = 0; i < steps.Count; i++)
            {
                float stepSize = steps[i];

                using (var builder = renderGraph.AddRasterRenderPass<JfaBlitPassData>(
                    "Lightweight Outline - JFA Step", out var passData))
                {
                    passData.source = jfaSrc;
                    passData.material = outlineMaterial;
                    passData.passIndex = PassJfaStep;
                    passData.step = stepSize;

                    builder.UseTexture(passData.source, AccessFlags.Read);
                    builder.UseGlobalTexture(OutlineMaskID, AccessFlags.Read);
                    builder.SetRenderAttachment(jfaDst, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);

                    if (i == steps.Count - 1)
                        builder.SetGlobalTextureAfterPass(jfaDst, JfaSeedID);

                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc(static (JfaBlitPassData data, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalFloat(JfaStepID, data.step);
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0),
                            data.material, data.passIndex);
                    });
                }

                (jfaSrc, jfaDst) = (jfaDst, jfaSrc);
            }

            // =====================================================
            // COMPOSITE: kamera rengine dogrudan alpha-blend.
            // Ayri full-res hedef yok, sahne rengi okunmuyor; outline
            // olmayan pikseller shader'da discard ile atiliyor.
            // =====================================================

            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                "Lightweight Outline - Composite", out var passData))
            {
                passData.material = outlineMaterial;

                builder.UseGlobalTexture(OutlineMaskID, AccessFlags.Read);
                builder.UseGlobalTexture(JfaSeedID, AccessFlags.Read);

                if (cameraDepthTexture.IsValid())
                    builder.UseTexture(cameraDepthTexture, AccessFlags.Read);

                builder.SetRenderAttachment(colorTarget, 0, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawProcedural(
                        Matrix4x4.identity, data.material, PassComposite,
                        MeshTopology.Triangles, 3, 1);
                });
            }
        }
    }
}