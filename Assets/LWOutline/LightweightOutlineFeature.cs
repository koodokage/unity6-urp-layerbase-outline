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
        public Color color = Color.black;

        [Min(0.5f)]
        public float width = 1.0f;

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
        Shader shader = Shader.Find("Hidden/Lightweight Outline");

        if (shader == null)
        {
            Debug.LogError(
                "Lightweight Outline shader not found. " +
                "Make sure LightweightOutline.shader exists."
            );

            return;
        }

        outlineMaterial = CoreUtils.CreateEngineMaterial(shader);
        maskMaterial = CoreUtils.CreateEngineMaterial(shader);

        outlinePass = new LightweightOutlinePass(
            outlineMaterial,
            maskMaterial
        );

        outlinePass.renderPassEvent = settings.renderPassEvent;
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

        // Avoid rendering on reflection / preview cameras.
        if (renderingData.cameraData.cameraType != CameraType.Game &&
            renderingData.cameraData.cameraType != CameraType.SceneView)
        {
            return;
        }

        outlinePass.Setup(
            settings.color,
            settings.width,
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

    private class LightweightOutlinePass : ScriptableRenderPass
    {
        private readonly Material outlineMaterial;
        private readonly Material maskMaterial;

        private Color outlineColor;
        private float outlineWidth;
        private LayerMask outlineLayerMask;

        private static readonly int OutlineColorID =
            Shader.PropertyToID("_OutlineColor");

        private static readonly int OutlineWidthID =
            Shader.PropertyToID("_OutlineWidth");

        private static readonly int OutlineMaskID =
            Shader.PropertyToID("_OutlineMask");

        // ---------------------------------------------------------
        // MASK PASS DATA
        // ---------------------------------------------------------

        private class MaskPassData
        {
            public RendererListHandle rendererList;
        }

        // ---------------------------------------------------------
        // OUTLINE PASS DATA
        // ---------------------------------------------------------

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
            LayerMask layerMask)
        {
            outlineColor = color;
            outlineWidth = Mathf.Max(0.5f, width);
            outlineLayerMask = layerMask;
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

            // -----------------------------------------------------
            // BACK BUFFER CHECK
            // -----------------------------------------------------

            // We need a readable camera color texture.
            if (resourceData.isActiveTargetBackBuffer)
            {
                Debug.LogWarning(
                    "Lightweight Outline skipped because the active " +
                    "camera target is the back buffer."
                );

                return;
            }

            // -----------------------------------------------------
            // 1. CREATE MASK TEXTURE
            // -----------------------------------------------------

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

            // -----------------------------------------------------
            // CREATE RENDERER LIST
            // -----------------------------------------------------

            FilteringSettings filteringSettings =
                new FilteringSettings(
                    RenderQueueRange.all,
                    outlineLayerMask
                );

            SortingCriteria sortingCriteria =
                cameraData.defaultOpaqueSortFlags;

            // Support the common URP shader pass names.
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

            // Draw selected objects using our simple mask material.
            drawingSettings.overrideMaterial =
                maskMaterial;

            drawingSettings.overrideMaterialPassIndex = 0;

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

            // -----------------------------------------------------
            // MASK RENDER PASS
            // -----------------------------------------------------

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

                // Color target = R8 mask.
                builder.SetRenderAttachment(
                    maskTexture,
                    0,
                    AccessFlags.Write
                );

                // Read the camera depth.
                // This makes the mask respect scene occlusion.
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
                        // Clear mask to black.
                        context.cmd.ClearRenderTarget(
                            false,
                            true,
                            Color.black
                        );

                        // Draw selected objects.
                        context.cmd.DrawRendererList(
                            data.rendererList
                        );
                    }
                );
            }

            // -----------------------------------------------------
            // 2. CREATE OUTLINE DESTINATION
            // -----------------------------------------------------

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

            // -----------------------------------------------------
            // SET MATERIAL PARAMETERS
            // -----------------------------------------------------

            outlineMaterial.SetColor(
                OutlineColorID,
                outlineColor
            );

            outlineMaterial.SetFloat(
                OutlineWidthID,
                outlineWidth
            );

            // -----------------------------------------------------
            // OUTLINE FULLSCREEN PASS
            // -----------------------------------------------------

            using (
                var builder =
                    renderGraph.AddRasterRenderPass<OutlinePassData>(
                        "Lightweight Outline - Composite",
                        out var passData
                    )
            )
            {
                passData.source = source;
                passData.mask = maskTexture;
                passData.material = outlineMaterial;

                // Read original camera color.
                builder.UseTexture(
                    passData.source,
                    AccessFlags.Read
                );

                // Read our mask.
                builder.UseTexture(
                    passData.mask,
                    AccessFlags.Read
                );

                // Write final image.
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
                            new Vector4(1f, 1f, 0f, 0f),
                            data.material,
                            1
                        );
                    }
                );
            }

            // -----------------------------------------------------
            // USE RESULT AS CAMERA COLOR
            // -----------------------------------------------------

            resourceData.cameraColor =
                destination;
        }
    }
}