using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class LightweightOutlineFeature : ScriptableRendererFeature
{
    // =============================================================
    // OCCLUSION
    // =============================================================

    public enum OutlineOcclusionMode
    {
        VisibleOnly,
        AlwaysVisible
    }

    // =============================================================
    // LAYER PROFILE
    // =============================================================

    [Serializable]
    public class OutlineLayerProfile
    {
        [Tooltip("MeshRenderer > Rendering Layer Mask index: 0-31")]
        [Range(0, 31)]
        public int renderingLayer = 0;

        [ColorUsage(true, true)]
        public Color color = Color.white;

        public OutlineOcclusionMode occlusionMode =
            OutlineOcclusionMode.VisibleOnly;

        [Tooltip("Bu layer'a ait outline kalinligi (piksel).")]
        [Min(0.5f)]
        public float width = 2.0f;
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
            Shader.Find("Hidden/Lightweight Outline");

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
        }

        if (renderingLayerMask == 0u)
            return;

        outlinePass.renderPassEvent =
            settings.renderPassEvent;

        outlinePass.Setup(
            renderingLayerMask,
            settings.layerProfiles
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

        // =========================================================
        // SHADER IDS
        // =========================================================

        private static readonly int OutlineMaskID =
            Shader.PropertyToID("_OutlineMask");

        private static readonly int SelectedDepthID =
            Shader.PropertyToID("_SelectedDepth");

        private static readonly int CameraDepthID =
            Shader.PropertyToID("_CameraDepthTexture");

        private static readonly int LayerColorID =
            Shader.PropertyToID("_LayerColors");

        private static readonly int LayerOcclusionID =
            Shader.PropertyToID("_LayerOcclusion");

        private static readonly int MaxOutlineWidthID =
            Shader.PropertyToID("_MaxOutlineWidth");

        // =========================================================
        // PASS DATA
        // =========================================================

        private class MaskPassData
        {
            public RendererListHandle rendererList;
        }

        private class SelectedDepthPassData
        {
            public RendererListHandle rendererList;
        }

        private class OutlinePassData
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
            this.outlineMaterial =
                outlineMaterial;

            this.maskMaterial =
                maskMaterial;
        }

        // =========================================================
        // SETUP
        // =========================================================

        public void Setup(
            uint renderingLayerMask,
            List<OutlineLayerProfile> layerProfiles)
        {
            this.renderingLayerMask =
                renderingLayerMask;

            this.layerProfiles =
                layerProfiles;
        }

        // =========================================================
        // DISPOSE
        // =========================================================

        public void Dispose()
        {
        }

        // =========================================================
        // PROFILE
        // =========================================================

        private void ApplyLayerProfiles()
        {
            Color[] colors =
                new Color[32];

            Vector4[] occlusion =
                new Vector4[32];

            /*
             * occlusion[i].x = occlusion mode (0/1)
             * occlusion[i].y = outline width (piksel)
             *
             * Width icin ayri bir float[] array KULLANILMIYOR:
             * HLSL constant buffer'da scalar float array
             * elemanlari 16 byte'a hizalanir, ama Unity'nin
             * SetFloatArray'i veriyi sikisik gonderir. Bu, ilk
             * eleman disindaki tum degerlerin bozuk okunmasina
             * yol aciyordu. float4 array (_LayerOcclusion) zaten
             * doğru calistigi icin width'i onun bos kanaliyla
             * (y) tasiyoruz.
             */

            for (int i = 0; i < 32; i++)
            {
                colors[i] =
                    Color.white;

                occlusion[i] =
                    new Vector4(0f, 2f, 0f, 0f);
            }

            float maxWidth =
                2.0f;

            if (layerProfiles != null)
            {
                for (int i = 0;
                     i < layerProfiles.Count;
                     i++)
                {
                    OutlineLayerProfile profile =
                        layerProfiles[i];

                    int layer =
                        Mathf.Clamp(
                            profile.renderingLayer,
                            0,
                            31
                        );

                    colors[layer] =
                        profile.color;

                    float width =
                        Mathf.Max(
                            0.5f,
                            profile.width
                        );

                    occlusion[layer] =
                        new Vector4(
                            profile.occlusionMode ==
                            OutlineOcclusionMode.AlwaysVisible
                                ? 1.0f
                                : 0.0f,
                            width,
                            0f,
                            0f
                        );

                    maxWidth =
                        Mathf.Max(
                            maxWidth,
                            width
                        );
                }
            }

            outlineMaterial.SetColorArray(
                LayerColorID,
                colors
            );

            outlineMaterial.SetVectorArray(
                LayerOcclusionID,
                occlusion
            );

            outlineMaterial.SetFloat(
                MaxOutlineWidthID,
                maxWidth
            );
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

            TextureHandle source =
                resourceData.activeColorTexture;

            TextureHandle cameraDepth =
                resourceData.activeDepthTexture;

            if (resourceData.isActiveTargetBackBuffer)
                return;

            // =====================================================
            // MASK
            // =====================================================

            RenderTextureDescriptor maskDescriptor =
                cameraData.cameraTargetDescriptor;

            maskDescriptor.msaaSamples = 1;
            maskDescriptor.depthBufferBits = 0;
            maskDescriptor.colorFormat =
                RenderTextureFormat.R8;
            maskDescriptor.sRGB = false;

            TextureHandle maskTexture =
                UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    maskDescriptor,
                    "Lightweight Outline Mask",
                    false
                );

            // =====================================================
            // SELECTED DEPTH
            // =====================================================

            RenderTextureDescriptor selectedDepthDescriptor =
                cameraData.cameraTargetDescriptor;

            selectedDepthDescriptor.msaaSamples = 1;
            selectedDepthDescriptor.depthBufferBits = 0;
            selectedDepthDescriptor.colorFormat =
                RenderTextureFormat.RFloat;
            selectedDepthDescriptor.sRGB = false;

            TextureHandle selectedDepthTexture =
                UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    selectedDepthDescriptor,
                    "Lightweight Outline Selected Depth",
                    false
                );

            // =====================================================
            // FILTERING
            // =====================================================

            FilteringSettings filteringSettings =
                new FilteringSettings(
                    RenderQueueRange.all,
                    -1,
                    renderingLayerMask,
                    0
                );

            SortingCriteria sortingCriteria =
                cameraData.defaultOpaqueSortFlags;

            List<ShaderTagId> shaderTags =
                new List<ShaderTagId>
                {
                    new ShaderTagId("UniversalForward"),
                    new ShaderTagId("UniversalForwardOnly"),
                    new ShaderTagId("SRPDefaultUnlit")
                };

            DrawingSettings drawingSettings =
                RenderingUtils.CreateDrawingSettings(
                    shaderTags,
                    renderingData,
                    cameraData,
                    lightData,
                    sortingCriteria
                );

            // =====================================================
            // MASK RENDERER LIST
            // =====================================================

            drawingSettings.overrideMaterial =
                maskMaterial;

            drawingSettings.overrideMaterialPassIndex =
                0;

            RendererListParams maskParams =
                new RendererListParams(
                    renderingData.cullResults,
                    drawingSettings,
                    filteringSettings
                );

            RendererListHandle maskRendererList =
                renderGraph.CreateRendererList(
                    maskParams
                );

            // =====================================================
            // SELECTED DEPTH RENDERER LIST
            // =====================================================

            drawingSettings.overrideMaterial =
                maskMaterial;

            drawingSettings.overrideMaterialPassIndex =
                1;

            RendererListParams depthParams =
                new RendererListParams(
                    renderingData.cullResults,
                    drawingSettings,
                    filteringSettings
                );

            RendererListHandle depthRendererList =
                renderGraph.CreateRendererList(
                    depthParams
                );

            // =====================================================
            // MASK PASS
            // =====================================================

            using (
                var builder =
                    renderGraph.AddRasterRenderPass<MaskPassData>(
                        "Lightweight Outline - Mask",
                        out var passData
                    )
            )
            {
                passData.rendererList =
                    maskRendererList;

                builder.UseRendererList(
                    passData.rendererList
                );

                builder.SetRenderAttachment(
                    maskTexture,
                    0,
                    AccessFlags.Write
                );

                builder.SetRenderAttachmentDepth(
                    cameraDepth,
                    AccessFlags.Read
                );

                builder.SetGlobalTextureAfterPass(
                    maskTexture,
                    OutlineMaskID
                );

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(
                    static (
                        MaskPassData data,
                        RasterGraphContext context
                    ) =>
                    {
                        context.cmd.ClearRenderTarget(
                            false,
                            true,
                            Color.black
                        );

                        context.cmd.DrawRendererList(
                            data.rendererList
                        );
                    }
                );
            }

            // =====================================================
            // SELECTED DEPTH PASS
            // =====================================================

            using (
                var builder =
                    renderGraph.AddRasterRenderPass<SelectedDepthPassData>(
                        "Lightweight Outline - Selected Depth",
                        out var passData
                    )
            )
            {
                passData.rendererList =
                    depthRendererList;

                builder.UseRendererList(
                    passData.rendererList
                );

                builder.SetRenderAttachment(
                    selectedDepthTexture,
                    0,
                    AccessFlags.Write
                );

                builder.SetGlobalTextureAfterPass(
                    selectedDepthTexture,
                    SelectedDepthID
                );

                builder.SetGlobalTextureAfterPass(
                    cameraDepth,
                    CameraDepthID
                );

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(
                    static (
                        SelectedDepthPassData data,
                        RasterGraphContext context
                    ) =>
                    {
                        context.cmd.ClearRenderTarget(
                            false,
                            true,
                            Color.black
                        );

                        context.cmd.DrawRendererList(
                            data.rendererList
                        );
                    }
                );
            }

            // =====================================================
            // DESTINATION
            // =====================================================

            RenderTextureDescriptor destinationDescriptor =
                cameraData.cameraTargetDescriptor;

            destinationDescriptor.msaaSamples = 1;
            destinationDescriptor.depthBufferBits = 0;

            TextureHandle destination =
                UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    destinationDescriptor,
                    "Lightweight Outline Result",
                    false
                );

            // =====================================================
            // MATERIAL
            // =====================================================

            ApplyLayerProfiles();

            // =====================================================
            // COMPOSITE
            // =====================================================

            using (
                var builder =
                    renderGraph.AddRasterRenderPass<OutlinePassData>(
                        "Lightweight Outline - Composite",
                        out var passData
                    )
            )
            {
                passData.source =
                    source;

                passData.material =
                    outlineMaterial;

                builder.UseTexture(
                    passData.source,
                    AccessFlags.Read
                );

                builder.UseGlobalTexture(
                    OutlineMaskID,
                    AccessFlags.Read
                );

                builder.UseGlobalTexture(
                    SelectedDepthID,
                    AccessFlags.Read
                );

                builder.UseGlobalTexture(
                    CameraDepthID,
                    AccessFlags.Read
                );

                builder.SetRenderAttachment(
                    destination,
                    0,
                    AccessFlags.Write
                );

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(
                    static (
                        OutlinePassData data,
                        RasterGraphContext context
                    ) =>
                    {
                        Blitter.BlitTexture(
                            context.cmd,
                            data.source,
                            new Vector4(
                                1f,
                                1f,
                                0f,
                                0f
                            ),
                            data.material,
                            2
                        );
                    }
                );
            }

            resourceData.cameraColor =
                destination;
        }
    }
}