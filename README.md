# Unity 6 URP Layer-Based Outline

A lightweight, layer-based outline effect for **Unity 6 Universal Render Pipeline (URP)**.

The effect is implemented as a URP `ScriptableRendererFeature` and runs during the rendering pipeline as a post-processing-style fullscreen pass. Objects on the configured layer mask are rendered into a mask, then `LightweightOutline.shader` samples that mask in screen space and composites the outline over the camera color.

## Features

- Compatible with **Unity 6** and **URP 17**
- Layer-based object selection
- Configurable outline color and width
- Depth-aware masking for scene occlusion
- Render Graph-compatible implementation
- Works as a post-processing-style fullscreen effect

## Structure

```text
Assets/
  LWOutline/
    LightweightOutlineFeature.cs       # URP renderer feature and render pass
    Resources/Shaders/
      LightweightOutline.shader        # Mask and fullscreen outline shader
  Scenes/SampleScene.unity              # Example scene
  Settings/                             # PC and mobile URP renderer settings
Packages/manifest.json                  # Unity package dependencies
ProjectSettings/ProjectVersion.txt      # Unity editor version
```

## Configuration

Configure `LightweightOutlineFeature` in the URP Renderer Data asset:

- **Layer Mask** — selects which objects receive the outline.
- **Color** — sets the outline color and alpha.
- **Width** — controls the screen-space outline width.
- **Render Pass Event** — controls when the effect is injected into the URP frame.

The included PC renderer configuration contains the outline feature. The shader uses a mask pass and an eight-direction screen-space sampling pass to detect object edges and composite the final outline.
