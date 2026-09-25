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


        [Header("Intersection")]

        public bool intersectionEnabled = true;

        [ColorUsage(true, true)]
        public Color intersectionColor = Color.red;

        [Min(0.0001f)]
        public float intersectionThreshold = 0.05f;

        [Min(0.5f)]
        public float intersectionWidth = 1.0f;


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
            settings.layerMask,

            settings.intersectionEnabled,
            settings.intersectionColor,
            settings.intersectionThreshold,
            settings.intersectionWidth
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


        private bool intersectionEnabled;
        private Color intersectionColor;

        private float intersectionThreshold;
        private float intersectionWidth;


        // =========================================================
        // SHADER PROPERTY IDS
        // =========================================================

        private static readonly int OutlineMaskID =
            Shader.PropertyToID("_OutlineMask");

        private static readonly int SelectedDepthID =
            Shader.PropertyToID("_SelectedDepth");

        private static readonly int CameraDepthID =
            Shader.PropertyToID("_CameraDepthTexture");


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


        private static readonly int IntersectionColorID =
            Shader.PropertyToID("_IntersectionColor");

        private static readonly int IntersectionThresholdID =
            Shader.PropertyToID("_IntersectionThreshold");

        private static readonly int IntersectionWidthID =
            Shader.PropertyToID("_IntersectionWidth");

        private static readonly int IntersectionEnabledID =
            Shader.PropertyToID("_IntersectionEnabled");


        // =========================================================
        // MASK PASS DATA
        // =========================================================

        private class MaskPassData
        {
            public RendererListHandle rendererList;
        }


        // =========================================================
        // SELECTED DEPTH PASS DATA
        // =========================================================

        private class SelectedDepthPassData
        {
            public RendererListHandle rendererList;
        }


        // =========================================================
        // COMPOSITE PASS DATA
        // =========================================================

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
            Color color,
            float width,
            float glowRadius,
            float glowIntensity,
            bool smoothCorners,
            LayerMask layerMask,

            bool intersectionEnabled,
            Color intersectionColor,
            float intersectionThreshold,
            float intersectionWidth)
        {
            outlineColor =
                color;

            outlineWidth =
                Mathf.Max(
                    0.5f,
                    width
                );


            this.glowRadius =
                Mathf.Max(
                    0.0f,
                    glowRadius
                );


            this.glowIntensity =
                Mathf.Max(
                    0.0f,
                    glowIntensity
                );


            this.smoothCorners =
                smoothCorners;


            outlineLayerMask =
                layerMask;


            this.intersectionEnabled =
                intersectionEnabled;


            this.intersectionColor =
                intersectionColor;


            this.intersectionThreshold =
                Mathf.Max(
                    0.0001f,
                    intersectionThreshold
                );


            this.intersectionWidth =
                Mathf.Max(
                    0.5f,
                    intersectionWidth
                );
        }


        // =========================================================
        // DISPOSE
        // =========================================================

        public void Dispose()
        {
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


            // =====================================================
            // SOURCE
            // =====================================================

            TextureHandle source =
                resourceData.activeColorTexture;


            // =====================================================
            // CAMERA DEPTH
            // =====================================================

            TextureHandle cameraDepth =
                resourceData.activeDepthTexture;


            // =====================================================
            // BACK BUFFER
            // =====================================================

            if (resourceData.isActiveTargetBackBuffer)
                return;


            // =====================================================
            // MASK TEXTURE
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
            // SELECTED DEPTH TEXTURE
            // =====================================================

      // =============================================================
// SELECTED DEPTH COLOR
// =============================================================

RenderTextureDescriptor selectedDepthColorDescriptor =
    cameraData.cameraTargetDescriptor;

selectedDepthColorDescriptor.msaaSamples = 1;
selectedDepthColorDescriptor.depthBufferBits = 0;
selectedDepthColorDescriptor.colorFormat =
    RenderTextureFormat.RFloat;
selectedDepthColorDescriptor.sRGB = false;


TextureHandle selectedDepthColor =
    UniversalRenderer.CreateRenderGraphTexture(
        renderGraph,
        selectedDepthColorDescriptor,
        "Lightweight Outline Selected Depth Color",
        false
    );


// =============================================================
// SELECTED DEPTH BUFFER
// =============================================================

RenderTextureDescriptor selectedDepthBufferDescriptor =
    cameraData.cameraTargetDescriptor;

selectedDepthBufferDescriptor.msaaSamples = 1;
selectedDepthBufferDescriptor.colorFormat =
    RenderTextureFormat.Depth;
selectedDepthBufferDescriptor.depthBufferBits = 32;
selectedDepthBufferDescriptor.sRGB = false;


TextureHandle selectedDepthBuffer =
    UniversalRenderer.CreateRenderGraphTexture(
        renderGraph,
        selectedDepthBufferDescriptor,
        "Lightweight Outline Selected Depth Buffer",
        false
    );


            // =====================================================
            // FILTERING
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


            // =====================================================
            // MASK RENDERER LIST
            // =====================================================

            drawingSettings.overrideMaterial =
                maskMaterial;

            drawingSettings.overrideMaterialPassIndex =
                0;


            RendererListParams maskRendererListParams =
                new RendererListParams(
                    renderingData.cullResults,
                    drawingSettings,
                    filteringSettings
                );


            RendererListHandle maskRendererList =
                renderGraph.CreateRendererList(
                    maskRendererListParams
                );


            // =====================================================
            // SELECTED DEPTH RENDERER LIST
            // =====================================================

            drawingSettings.overrideMaterial =
                maskMaterial;

            drawingSettings.overrideMaterialPassIndex =
                1;


            RendererListParams selectedDepthRendererListParams =
                new RendererListParams(
                    renderingData.cullResults,
                    drawingSettings,
                    filteringSettings
                );


            RendererListHandle selectedDepthRendererList =
                renderGraph.CreateRendererList(
                    selectedDepthRendererListParams
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


                /*
                 * Use the actual camera depth.
                 *
                 * Objects outside the LayerMask can therefore
                 * occlude the selected object.
                 */
                builder.SetRenderAttachmentDepth(
                    cameraDepth,
                    AccessFlags.Read
                );


                /*
                 * Make the mask available to later passes.
                 */
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


          // =============================================================
// SELECTED DEPTH PASS
// =============================================================

using (
    var builder =
        renderGraph.AddRasterRenderPass<SelectedDepthPassData>(
            "Lightweight Outline - Selected Depth",
            out var passData
        )
)
{
    passData.rendererList =
        selectedDepthRendererList;


    builder.UseRendererList(
        passData.rendererList
    );


    // RFloat color target
    builder.SetRenderAttachment(
        selectedDepthColor,
        0,
        AccessFlags.Write
    );


    // Separate hardware depth buffer
    builder.SetRenderAttachmentDepth(
        selectedDepthBuffer,
        AccessFlags.Write
    );


    // Selected object's linear depth
    builder.SetGlobalTextureAfterPass(
        selectedDepthColor,
        SelectedDepthID
    );


    // Camera depth
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
                true,
                true,
                Color.clear
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
            // MATERIAL SETTINGS
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
                smoothCorners
                    ? 1.0f
                    : 0.0f
            );


            outlineMaterial.SetColor(
                IntersectionColorID,
                intersectionColor
            );


            outlineMaterial.SetFloat(
                IntersectionThresholdID,
                intersectionThreshold
            );


            outlineMaterial.SetFloat(
                IntersectionWidthID,
                intersectionWidth
            );


            outlineMaterial.SetFloat(
                IntersectionEnabledID,
                intersectionEnabled
                    ? 1.0f
                    : 0.0f
            );


            // =====================================================
            // COMPOSITE PASS
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


                /*
                 * These were registered as global textures
                 * in the previous passes.
                 */
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
                        /*
                         * No context.resources.
                         *
                         * No SetTexture().
                         *
                         * RenderGraph has already bound:
                         *
                         * _OutlineMask
                         * _SelectedDepth
                         * _CameraDepthTexture
                         */
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


            // =====================================================
            // FINAL CAMERA COLOR
            // =====================================================

            resourceData.cameraColor =
                destination;
        }
    }
}