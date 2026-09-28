# Unity 6 URP Layer-Based Outline

A lightweight fullscreen outline effect for Unity 6 Universal Render Pipeline (URP). This project uses a Render Layer Mask-based selection system and a Jump Flood Algorithm (JFA) to generate clean outlines around specific objects in the scene.

## Performance
- Machine: Apple MacBook M1 (2019) or i5 & Nvidia GeForce GTX 1650 Super
- Scene: 500 Primitive Objects
- Resolution: Full HD
- With Outline : 167 FPS
- Without Outline : 197 FPS

<img width="1832" height="986" alt="ezgif com-gif-maker" src="https://github.com/user-attachments/assets/b0aec635-4940-4711-a9b1-c09052d78805" />

<img width="1434" height="759" alt="image" src="https://github.com/user-attachments/assets/cd9e3d12-ee04-47c8-a844-2a4b0a1fcfdf" />

## Features

- Render Layer Mask-based object selection
- Per-layer outline color, width, render distance, and depth behavior
- Jump Flood Algorithm (JFA) for efficient edge detection
- Full-resolution outline compositing after JFA
- Depth-aware occlusion support with optional X-Ray behavior
- HDR color support
- Resolution scaling for performance tuning
- URP ScriptableRendererFeature integration

- ## [Extra] Additive LWRendererOutline

**LWRendererOutline** is an optimized, lightweight alternative outline solution that uses a transparent back-face rendering technique, which you add as an extra material to the existing URP renderer component.

<img width="2539" height="887" alt="image" src="https://github.com/user-attachments/assets/a976f980-59b4-4d56-bd70-310cea9546b7" />

## Sample Scene

The project includes a ready-to-use sample scene:
- `Assets/Scenes/SampleScene.unity`

It demonstrates multiple outlined layers with different colors and widths in a basic URP setup.

## URP Renderer Setup

The repo includes example URP renderer assets:
- `Assets/Settings/PC_Renderer.asset`
- `Assets/Settings/Mobile_Renderer.asset`

The PC renderer configuration already contains the outline feature and a multi-layer profile setup.

## How It Works

This effect is split into three main stages:

1. Mask Pass
   - Renders selected objects into a low-resolution mask
   - Applies render distance filtering
   - Stores layer information and scene depth

2. JFA Pass
   - Runs Jump Flood propagation on the mask texture
   - Finds the nearest valid outline seed for each pixel

3. Composite Pass
   - Reads the JFA result at full resolution
   - Draws the final outline and applies layer-specific depth checks

## Configuration

Each layer can be configured with:
- Render Layer Mask index
- Color
- Width
- Maximum render distance
- Depth test enabled/disabled
- Depth bias

This is handled by the `LightweightOutlineFeature` renderer feature.


## Project Structure

```text
Assets/
  LWOutline/
    LightweightOutlineFeature.cs
    Resources/
      Shaders/
        Lightweight Fullscreen Outline.shader
        LWRendererOutline.shader
  Scenes/
    SampleScene.unity
  Settings/
    PC_Renderer.asset
    Mobile_Renderer.asset
