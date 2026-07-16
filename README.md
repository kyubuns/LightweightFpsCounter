# LightweightFpsCounter

**A simple, fast FPS counter for Unity.**

A minimal FPS counter for the Universal Render Pipeline (URP), built with an obsessive focus on low runtime overhead.

[日本語版 README はこちら](README.ja.md)

<img width="332" height="162" alt="LightweightFpsCounter" src="https://github.com/user-attachments/assets/19db7233-5a9b-42a8-bd29-27e40de68377" />

`NOW` is the latest sample; `AVG` is a 1-second average.

## Features

- Shows FPS plus CPU total / main thread / present wait / render thread / GPU frame times, measured by `FrameTimingManager`.
- Each metric can be shown or hidden, and every label is freely editable.
- Values can change to warning/error colors, e.g. when FPS drops below or a frame time rises above your thresholds.
- Frames averaged into each NOW update, text scale, screen-corner anchor, margin and colors are all adjustable.
- The bitmap font can be replaced with your own atlas of any glyph / cell dimensions.
- Metrics are also readable from code via static properties such as `LightweightFpsCounterHud.LatestFps`.
- Zero GC allocations at runtime.

## Requirements

- Unity 6.3 or newer. Built for the Universal Render Pipeline (URP), though it contains no pipeline-specific code.
- "Frame Timing Stats" enabled in Player Settings, otherwise all values read `0`.
- Some timings are unavailable on some platforms; see the [FrameTimingManager documentation](https://docs.unity3d.com/ScriptReference/FrameTimingManager.html) for details.

## Installation

1. In the Package Manager, choose *Install package from git URL...* and enter `https://github.com/kyubuns/LightweightFpsCounter.git?path=Assets/LightweightFpsCounter`.
2. Place the bundled **LightweightFpsCounterHud** prefab in your first scene.

## Release builds

The measurement and rendering implementation is compiled only in the editor and Development Builds; non-development builds contain only an inactive component stub.
Define `FPS_COUNTER_ENABLE_IN_RELEASE` if you intentionally want the counter in release builds.

The component's serialized fields remain present in every build configuration to keep the serialized layout compatible between Addressables content builds and Player builds. Therefore, if a **LightweightFpsCounterHud** prefab is included in a Release build, its referenced assets, including the font texture and overlay shader, are also included even though the counter is inactive.

To keep the counter and its assets completely out of Release builds, exclude the prefab from every build input used by your project, such as Addressables, `Resources`, and Scenes. One option is to put the prefab in a Development-only Addressables Group and disable **Include In Build** for that group in the Release Addressables profile/build settings.

## How it stays fast

- Within one mesh, the static header, labels, and `ms` region is rebuilt only when settings change; digits occupy fixed-width slots.
- Frame timings are fetched in configurable batches and NOW is updated with their average.
- NOW and AVG digit slots occupy separate contiguous ranges. Regular refreshes rewrite and partially upload only the NOW UV range with validation-skipping `MeshUpdateFlags`.
- Vertex colors are re-uploaded only when a value crosses a threshold.
- Static text and dynamic digits share one mesh with separate vertex-attribute streams. Refreshes upload only the dynamic UV range.
- The draw is pre-recorded in a command buffer, leaving one `ExecuteCommandBuffer` call at the end of the frame. Indices are 16-bit, with no pipeline hooks, culling, or sorting.
- The vertex shader maps pixel coordinates straight to clip space, so anchoring costs nothing on the CPU.

## Font

The bundled font is [monogram](https://datagoblin.itch.io/monogram) by datagoblin (`Monogram.png`, CC0).
Any bitmap atlas holding ASCII 32..126 works: adjust **Glyph Size**, **Cell Size**, **Atlas Origin**, **Atlas Columns**, **Letter Spacing** and **Line Height** to match your texture.
Import custom atlases with Point filtering, no compression, no mipmaps and Non-Power of 2 set to None.

## Credits

- Code generated with Claude Fable 5 and ChatGPT 5.6.
- Font: [monogram](https://datagoblin.itch.io/monogram) by datagoblin (CC0).
