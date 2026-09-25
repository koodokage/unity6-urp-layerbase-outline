using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class LightweightOutlineFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Outline")]
        [ColorUsage(true, true)]
        public Color color = Color.black;

        [Min(0.5f)]
        public float width = 1.0f;

        [Header("Glow")]
        [Min(0.0f)]
        public float glowRadius = 2.0f;

        [Min(0.0f)]
        public float glowIntensity = 1.0f;

        [Header("Quality")]
        public bool smoothCorners = true;

        [Header("Layer Mask")]
        public LayerMask layerMask = 0;

        [Header("Render Pass")]
        public RenderPassEvent renderPassEvent =
            RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public Settings settings = new Settings();

    private Material outlineMaterial;
    private Material maskMaterial;

    private LightweightOutlinePass outlinePass;

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

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        if (outlinePass == null)
            return;

        if (settings.layerMask.value == 0)
            return;

        if (renderingData.cameraData.isPreviewCamera)
            return;

        CameraType cameraType =
            renderingData.cameraData.cameraType;

        if (cameraType != CameraType.Game &&
            cameraType != CameraType.SceneView)
        {
            return;
        }

        outlinePass.Setup(
            settings.color,
            settings.width,
            settings.glowRadius,
            settings.glowIntensity,
            settings.smoothCorners,
            settings.layerMask
        );

        renderer.EnqueuePass(outlinePass);
    }

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
    // OUTLINE PASS
    // =============================================================

    private class LightweightOutlinePass : ScriptableRenderPass
    {
        private readonly Material outlineMaterial;
        private readonly Material maskMaterial;

        private Color outlineColor;

        private float outlineWidth;
        private float glowRadius;
        private float glowIntensity;

        private bool smoothCorners;

        private LayerMask outlineLayerMask;

        private static readonly int OutlineColorID =
            Shader.PropertyToID("_OutlineColor");

        private static readonly int OutlineWidthID =
            Shader.PropertyToID("_OutlineWidth");

        private static readonly int GlowRadiusID =
            Shader.PropertyToID("_GlowRadius");

        private static readonly int GlowIntensityID =
            Shader.PropertyToID("_GlowIntensity");

        private static readonly int SmoothCornersID =
            Shader.PropertyToID("_SmoothCorners");

        private static readonly int OutlineMaskID =
            Shader.PropertyToID("_OutlineMask");

        // =========================================================
        // MASK PASS DATA
        // =========================================================

        private class MaskPassData
        {
            public RendererListHandle rendererList;
        }

        // =========================================================
        // OUTLINE PASS DATA
        // =========================================================

        private class OutlinePassData
        {
            public TextureHandle source;
            public TextureHandle mask;
            public Material material;
        }

        public LightweightOutlinePass(
            Material outlineMaterial,
            Material maskMaterial)
        {
            this.outlineMaterial = outlineMaterial;
            this.maskMaterial = maskMaterial;
        }

        public void Setup(
            Color color,
            float width,
            float glowRadius,
            float glowIntensity,
            bool smoothCorners,
            LayerMask layerMask)
        {
            outlineColor = color;

            outlineWidth =
                Mathf.Max(0.5f, width);

            this.glowRadius =
                Mathf.Max(0.0f, glowRadius);

            this.glowIntensity =
                Mathf.Max(0.0f, glowIntensity);

            this.smoothCorners =
                smoothCorners;

            outlineLayerMask =
                layerMask;
        }

        public void Dispose()
        {
        }

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

            TextureHandle depth =
                resourceData.activeDepthTexture;

            // =====================================================
            // BACK BUFFER CHECK
            // =====================================================

            if (resourceData.isActiveTargetBackBuffer)
            {
                return;
            }

            // =====================================================
            // MASK TEXTURE
            // =====================================================

            RenderTextureDescriptor maskDescriptor =
                cameraData.cameraTargetDescriptor;

            maskDescriptor.width =
                cameraData.cameraTargetDescriptor.width;

            maskDescriptor.height =
                cameraData.cameraTargetDescriptor.height;

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
            // RENDERER LIST
            // =====================================================

            FilteringSettings filteringSettings =
                new FilteringSettings(
                    RenderQueueRange.all,
                    outlineLayerMask
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

            drawingSettings.overrideMaterial =
                maskMaterial;

            drawingSettings.overrideMaterialPassIndex =
                0;

            RendererListParams rendererListParams =
                new RendererListParams(
                    renderingData.cullResults,
                    drawingSettings,
                    filteringSettings
                );

            RendererListHandle rendererList =
                renderGraph.CreateRendererList(
                    rendererListParams
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
                    rendererList;

                builder.UseRendererList(
                    passData.rendererList
                );

                builder.SetRenderAttachment(
                    maskTexture,
                    0,
                    AccessFlags.Write
                );

                builder.SetRenderAttachmentDepth(
                    depth,
                    AccessFlags.Read
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
            // MATERIAL PARAMETERS
            // =====================================================

            outlineMaterial.SetColor(
                OutlineColorID,
                outlineColor
            );

            outlineMaterial.SetFloat(
                OutlineWidthID,
                outlineWidth
            );

            outlineMaterial.SetFloat(
                GlowRadiusID,
                glowRadius
            );

            outlineMaterial.SetFloat(
                GlowIntensityID,
                glowIntensity
            );

            outlineMaterial.SetFloat(
                SmoothCornersID,
                smoothCorners ? 1.0f : 0.0f
            );

            // =====================================================
            // FULLSCREEN OUTLINE
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

                passData.mask =
                    maskTexture;

                passData.material =
                    outlineMaterial;

                builder.UseTexture(
                    passData.source,
                    AccessFlags.Read
                );

                builder.UseTexture(
                    passData.mask,
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
                        data.material.SetTexture(
                            OutlineMaskID,
                            data.mask
                        );

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
                            1
                        );
                    }
                );
            }

            // =====================================================
            // FINAL CAMERA COLOR
            // =====================================================

            resourceData.cameraColor =
                destination;
        }
    }
}