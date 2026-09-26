using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class LightweightOutlineFeature : ScriptableRendererFeature
{
    // =============================================================
    // LAYER PROFILE
    // =============================================================

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
            "Acik: bu layer normal derinlik testi kullanir; obje baska bir " +
            "seyin (duvar vb.) arkasina girdiginde outline kaybolur.\n" +
            "Kapali: X-Ray modu; outline derinlikten bagimsiz her zaman gorunur."
        )]
        public bool depthTest = true;

        [Tooltip(
            "Depth Test acikken kullanilan bias (world units, metre). " +
            "Mask pass dusuk cozunurlukte calistigi icin siluet kenarlarinda " +
            "olusabilecek yanlis-occlusion titremesini onlemek icin kucuk " +
            "bir tolerans eklenir."
        )]
        [Min(0f)]
        public float depthBias = 0.05f;
    }

    // =============================================================
    // SETTINGS
    // =============================================================

    [Serializable]
    public class Settings
    {
        [Header("Rendering Layers")]

        [Tooltip(
            "Outline cizilecek her Rendering Layer icin bir profil " +
            "ekleyin. Rendering Layer Mask buradan otomatik hesaplanir."
        )]
        public List<OutlineLayerProfile> layerProfiles = new();

        [Header("Performance")]

        [Tooltip(
            "Mask / depth / Jump-Flood hesaplamalarinin yapildigi cozunurluk " +
            "(kamera hedefine oranla). Dusuk deger = cok daha hizli, " +
            "kenarlar biraz daha yumusak/kaba olur. Composite her zaman " +
            "tam cozunurlukte yapilir."
        )]
        [Range(0.25f, 1f)]
        public float resolutionScale = 0.5f;

        [Tooltip(
            "JFA sonunda kalite icin eklenen ekstra step=1 duzeltme pass sayisi. " +
            "0-2 arasi genelde yeterli."
        )]
        [Range(0, 2)]
        public int extraRefinementPasses = 1;

        [Header("Render Pass")]

        public RenderPassEvent renderPassEvent =
            RenderPassEvent.BeforeRenderingPostProcessing;
    }

    // =============================================================
    // FEATURE
    // =============================================================

    public Settings settings = new();

    private Material outlineMaterial;
    private Material maskMaterial;

    private LightweightOutlinePass outlinePass;

    // =============================================================
    // CREATE
    // =============================================================

    public override void Create()
    {
        Shader shader =
            Shader.Find("Hidden/Lightweight Fullscreen Outline");

        if (shader == null)
        {
            Debug.LogError(
                "Lightweight Outline shader not found."
            );

            return;
        }

        outlineMaterial =
            CoreUtils.CreateEngineMaterial(shader);

        maskMaterial =
            CoreUtils.CreateEngineMaterial(shader);

        outlinePass =
            new LightweightOutlinePass(
                outlineMaterial,
                maskMaterial
            );

        outlinePass.renderPassEvent =
            settings.renderPassEvent;

        // Per-layer depth test icin Mask pass'te _CameraDepthTexture
        // sample ediyoruz; bu, URP Asset'teki "Depth Texture" checkbox'i
        // kapali olsa bile gerekli copy-depth pass'inin calismasini
        // garanti eder.
        outlinePass.ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    // =============================================================
    // ADD PASS
    // =============================================================

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        if (outlinePass == null)
            return;

        if (settings.layerProfiles == null ||
            settings.layerProfiles.Count == 0)
        {
            return;
        }

        if (renderingData.cameraData.isPreviewCamera)
            return;

        CameraType cameraType =
            renderingData.cameraData.cameraType;

        if (cameraType != CameraType.Game &&
            cameraType != CameraType.SceneView)
        {
            return;
        }

        // =====================================================
        // MASK LISTEDEN OTOMATIK HESAPLANIR
        // =====================================================

        uint renderingLayerMask = 0u;
        float maxWidth = 0f;

        for (int i = 0; i < settings.layerProfiles.Count; i++)
        {
            int layer =
                Mathf.Clamp(
                    settings.layerProfiles[i].renderingLayer,
                    0,
                    31
                );

            renderingLayerMask |=
                (1u << layer);

            maxWidth =
                Mathf.Max(maxWidth, settings.layerProfiles[i].width);
        }

        if (renderingLayerMask == 0u)
            return;

        outlinePass.renderPassEvent =
            settings.renderPassEvent;

        outlinePass.Setup(
            renderingLayerMask,
            settings.layerProfiles,
            Mathf.Clamp(settings.resolutionScale, 0.25f, 1f),
            Mathf.Max(1f, maxWidth),
            Mathf.Clamp(settings.extraRefinementPasses, 0, 2)
        );

        renderer.EnqueuePass(outlinePass);
    }

    // =============================================================
    // DISPOSE
    // =============================================================

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

    // =============================================================
    // PASS
    // =============================================================

    private class LightweightOutlinePass : ScriptableRenderPass
    {
        private readonly Material outlineMaterial;
        private readonly Material maskMaterial;

        private uint renderingLayerMask;
        private List<OutlineLayerProfile> layerProfiles;

        private float resolutionScale = 0.5f;
        private float maxWidth = 8f;
        private int extraRefinementPasses = 1;

        // Her kare yeniden allocate etmemek icin cache'lenmis buffer'lar
        private readonly Color[] cachedColors = new Color[32];
        private readonly Vector4[] cachedWidths = new Vector4[32];
        private readonly Vector4[] cachedRenderDistance = new Vector4[32];
        private readonly Vector4[] cachedDepthTest = new Vector4[32];
        private readonly List<int> cachedSteps = new List<int>(16);

        private static readonly List<ShaderTagId> ShaderTags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        // =========================================================
        // SHADER PASS INDICES
        // =========================================================

        private const int PassMask = 0;
        private const int PassJfaInit = 1;
        private const int PassJfaStep = 2;
        private const int PassComposite = 3;

        // =========================================================
        // SHADER IDS
        // =========================================================

        private static readonly int OutlineMaskID =
            Shader.PropertyToID("_OutlineMask");

        private static readonly int JfaSeedID =
            Shader.PropertyToID("_JFASeedTex");

        private static readonly int JfaStepID =
            Shader.PropertyToID("_JFAStep");

        private static readonly int LayerColorID =
            Shader.PropertyToID("_LayerColors");

        private static readonly int LayerWidthID =
            Shader.PropertyToID("_LayerWidths");

        private static readonly int LayerRenderDistanceID =
            Shader.PropertyToID("_LayerRenderDistance");

        private static readonly int LayerDepthTestID =
            Shader.PropertyToID("_LayerDepthTest");

        // =========================================================
        // PASS DATA
        // =========================================================

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
            public TextureHandle source;
            public Material material;
        }

        // =========================================================
        // CONSTRUCTOR
        // =========================================================

        public LightweightOutlinePass(
            Material outlineMaterial,
            Material maskMaterial)
        {
            this.outlineMaterial = outlineMaterial;
            this.maskMaterial = maskMaterial;
        }

        // =========================================================
        // SETUP
        // =========================================================

        public void Setup(
            uint renderingLayerMask,
            List<OutlineLayerProfile> layerProfiles,
            float resolutionScale,
            float maxWidth,
            int extraRefinementPasses)
        {
            this.renderingLayerMask = renderingLayerMask;
            this.layerProfiles = layerProfiles;
            this.resolutionScale = resolutionScale;
            this.maxWidth = maxWidth;
            this.extraRefinementPasses = extraRefinementPasses;
        }

        public void Dispose() { }

        // =========================================================
        // PROFILE -> MATERIAL ARRAYS
        // =========================================================

        private void ApplyLayerProfiles()
        {
            for (int i = 0; i < 32; i++)
            {
                cachedColors[i] = Color.white;
                cachedWidths[i] = new Vector4(2f, 0f, 0f, 0f);
                cachedRenderDistance[i] = new Vector4(0f, 0f, 0f, 0f); // 0 = sinirsiz
                cachedDepthTest[i] = new Vector4(0f, 0.05f, 0f, 0f); // x=0 (kapali/X-Ray), y=bias
            }

            if (layerProfiles != null)
            {
                for (int i = 0; i < layerProfiles.Count; i++)
                {
                    OutlineLayerProfile profile = layerProfiles[i];

                    int layer = Mathf.Clamp(profile.renderingLayer, 0, 31);

                    cachedColors[layer] = profile.color;

                    float width = Mathf.Max(0.5f, profile.width);

                    // Not: width'i tek basina float[] olarak gondermiyoruz,
                    // cunku HLSL constant buffer'da scalar float array
                    // elemanlari 16 byte'a hizalanir ama Unity'nin
                    // SetFloatArray'i veriyi sikisik gonderir; bu da ilk
                    // eleman disindaki degerlerin bozuk okunmasina yol
                    // aciyor. Vector4 array (x = width) bu sorunu yasamaz.
                    cachedWidths[layer] = new Vector4(width, 0f, 0f, 0f);

                    cachedRenderDistance[layer] =
                        new Vector4(Mathf.Max(0f, profile.renderDistance), 0f, 0f, 0f);

                    // x = depth test acik mi (1/0), y = bias (world units)
                    cachedDepthTest[layer] = new Vector4(
                        profile.depthTest ? 1f : 0f,
                        Mathf.Max(0f, profile.depthBias),
                        0f,
                        0f
                    );
                }
            }

            outlineMaterial.SetColorArray(LayerColorID, cachedColors);
            outlineMaterial.SetVectorArray(LayerWidthID, cachedWidths);

            // Per-layer occlusion karari artik Composite pass'te
            // (outlineMaterial) uygulaniyor - Mask pass'te DEGIL, cunku
            // mask'in TAM/kesintisiz siluet olarak kalmasi JFA'nin
            // dugumlenme/cift-outline artifact'i uretmemesi icin sart.
            outlineMaterial.SetVectorArray(LayerDepthTestID, cachedDepthTest);

            // Mesafe kesme islemi Mask pass'te (maskMaterial) yapiliyor,
            // boylece uzaktaki objeler icin JFA/Composite'e hic seed
            // gitmiyor.
            maskMaterial.SetVectorArray(LayerRenderDistanceID, cachedRenderDistance);
        }

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

            // Genislik full-res piksel cinsinden; JFA texture'i scaled
            // cozunurlukte oldugu icin ihtiyac duyulan yayilma yaricapini
            // (texel cinsinden) scaled uzaya cevirmemiz gerekiyor.
            int radiusTexels =
                Mathf.CeilToInt(maxWidth * resolutionScale);

            radiusTexels = Mathf.Clamp(radiusTexels, 1, Mathf.Max(scaledWidth, scaledHeight));

            int n = NextPow2(radiusTexels);

            for (int s = n; s >= 1; s >>= 1)
                cachedSteps.Add(s);

            for (int i = 0; i < extraRefinementPasses; i++)
                cachedSteps.Add(1);

            return cachedSteps;
        }

        // =========================================================
        // RENDER GRAPH
        // =========================================================

        public override void RecordRenderGraph(
            RenderGraph renderGraph,
            ContextContainer frameData)
        {
            UniversalResourceData resourceData =
                frameData.Get<UniversalResourceData>();

            UniversalRenderingData renderingData =
                frameData.Get<UniversalRenderingData>();

            UniversalCameraData cameraData =
                frameData.Get<UniversalCameraData>();

            UniversalLightData lightData =
                frameData.Get<UniversalLightData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            // Mask pass (mesafe/derinlik kesme) ve Composite pass
            // (renk/genislik) materyal ozelliklerine ihtiyac duydugu icin
            // herhangi bir pass eklenmeden once uygulanmali.
            ApplyLayerProfiles();

            TextureHandle source = resourceData.activeColorTexture;

            // Per-layer depth test icin sahne depth texture'ina ihtiyacimiz
            // var (Opaque pass'ten sonra populate edilmis olmali - bu yuzden
            // renderPassEvent varsayilani BeforeRenderingPostProcessing).
            // Mask pass'te bunu bir depth-stencil ATTACHMENT olarak degil,
            // fragment shader icinde SampleSceneDepth() ile TEXTURE olarak
            // okuyoruz (_CameraDepthTexture) - bu yuzden dogru RenderGraph
            // resource'u activeDepthTexture degil, cameraDepthTexture'dir.
            TextureHandle cameraDepthTexture = resourceData.cameraDepthTexture;

            RenderTextureDescriptor fullDesc = cameraData.cameraTargetDescriptor;

            int scaledW = Mathf.Max(1, Mathf.RoundToInt(fullDesc.width * resolutionScale));
            int scaledH = Mathf.Max(1, Mathf.RoundToInt(fullDesc.height * resolutionScale));

            // =====================================================
            // MASK (scaled res). Per-layer depth test artik Mask pass
            // icinde, _CameraDepthTexture manuel sample edilerek
            // uygulaniyor (hardware ZTest attachment DEGIL, cunku mask
            // hedefi scaled cozunurlukte ve gercek depth buffer'la
            // boyut olarak eslesmiyor).
            // =====================================================

            RenderTextureDescriptor maskDescriptor = fullDesc;
            maskDescriptor.width = scaledW;
            maskDescriptor.height = scaledH;
            maskDescriptor.msaaSamples = 1;
            maskDescriptor.depthBufferBits = 0;
            // R = layerID/255, G = o siluet noktasinin kendi device-space
            // derinligi (Composite'teki per-segment occlusion kontrolu
            // icin). Tek kanal yetmedigi icin R8'den RGFloat'a gecildi.
            maskDescriptor.colorFormat = RenderTextureFormat.RGFloat;
            maskDescriptor.sRGB = false;

            TextureHandle maskTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, maskDescriptor, "Lightweight Outline Mask", false);

            // =====================================================
            // JFA PING-PONG (scaled res, RGFloat = seed UV, x<0 => empty)
            // =====================================================

            RenderTextureDescriptor jfaDescriptor = fullDesc;
            jfaDescriptor.width = scaledW;
            jfaDescriptor.height = scaledH;
            jfaDescriptor.msaaSamples = 1;
            jfaDescriptor.depthBufferBits = 0;
            jfaDescriptor.colorFormat = RenderTextureFormat.RGFloat;
            jfaDescriptor.sRGB = false;

            TextureHandle jfaA = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, jfaDescriptor, "Lightweight Outline JFA A", false);

            TextureHandle jfaB = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, jfaDescriptor, "Lightweight Outline JFA B", false);

            // =====================================================
            // FILTERING / DRAWING SETTINGS
            // =====================================================

            FilteringSettings filteringSettings = new FilteringSettings(
                RenderQueueRange.all, -1, renderingLayerMask, 0);

            SortingCriteria sortingCriteria = cameraData.defaultOpaqueSortFlags;

            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                ShaderTags, renderingData, cameraData, lightData, sortingCriteria);

            // ---- Mask renderer list
            drawingSettings.overrideMaterial = maskMaterial;
            drawingSettings.overrideMaterialPassIndex = PassMask;

            RendererListHandle maskRendererList = renderGraph.CreateRendererList(
                new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings));

            // =====================================================
            // MASK PASS
            // =====================================================

            using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>(
                "Lightweight Outline - Mask", out var passData))
            {
                passData.rendererList = maskRendererList;

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(maskTexture, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(maskTexture, OutlineMaskID);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(false, true, Color.black);
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }

            // =====================================================
            // JFA INIT: maskTexture -> jfaA (seed = kendi UV'si ya da -1,-1)
            // =====================================================

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

            // =====================================================
            // JFA STEPS (ping-pong jfaA <-> jfaB)
            // =====================================================

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

            // _JFASeedTex global'i artik son JFA step'in ciktisina isaret ediyor.

            // =====================================================
            // COMPOSITE (full res)
            // =====================================================

            RenderTextureDescriptor destinationDescriptor = fullDesc;
            destinationDescriptor.msaaSamples = 1;
            destinationDescriptor.depthBufferBits = 0;

            TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, destinationDescriptor, "Lightweight Outline Result", false);

            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                "Lightweight Outline - Composite", out var passData))
            {
                passData.source = source;
                passData.material = outlineMaterial;

                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.UseGlobalTexture(OutlineMaskID, AccessFlags.Read);
                builder.UseGlobalTexture(JfaSeedID, AccessFlags.Read);

                // Composite pass, per-layer occlusion karari icin sahne
                // depth texture'ini (_CameraDepthTexture) sample ediyor -
                // RenderGraph'a bu bagimliligi burada bildiriyoruz.
                if (cameraDepthTexture.IsValid())
                {
                    builder.UseTexture(cameraDepthTexture, AccessFlags.Read);
                }

                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0),
                        data.material, PassComposite);
                });
            }

            resourceData.cameraColor = destination;
        }
    }
}