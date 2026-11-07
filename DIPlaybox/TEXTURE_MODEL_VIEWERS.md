# Texture and model previews

Use **Open texture / model** for loose resources, or double-click a texture/model entry in an open archive. The main Open dialog, drag-and-drop, and command-line opening also recognize these resources.

## Textures

- `.tbody`, `.tstream`, `.dds`, and PNG are detected by their bytes.
- PC DDS: BC1/DXT1, BC3/DXT5, and reviewed 32-bit RGBA/BGRA channel masks.
- Wii U Gfx2: the existing native BC1/BC3 decoder and reviewed GX2 tile modes 0, 1, 2, and 4.
- Fit and 1:1 views, dimensions/format, and export to a new PNG copy.
- A resolvable, independently decodable higher-resolution TSTREAM is selected when larger than the base. Unsupported streams retain the base and display the reason. Raw stream variants that require additional reconstruction are not claimed supported.

Image input is limited to 64 MiB and 16,777,216 pixels. Other formats have explicit unsupported diagnostics; original resource extraction remains available.

## Models

Model-bearing `.oct` files use the reviewed AssetWiiUdio reader for PC and Wii U, including parent scene placement and companion `.vbuf`/`.ibuf` geometry. Animation/effect OCTs are not models and may be rejected.

- Drag with the left mouse button to orbit; use the wheel to zoom; Reset view fits the model.
- Default colour texture is resolved through the matching MTB's material reference and known shader sampler layout.
- Use **Texture assets folder** in the main window to choose a game root, assets directory, or extracted texture folder. The choice is remembered locally. Reopen a preview after changing it.
- Texture lookup checks the open archive, scoped/loose textures, and the exact two-hex texture archive (for example `textures/58.zip`). Ambiguous matches are rejected; there is no recursive full-library scan.
- Source normals are used where reviewed; otherwise WPF computes normals and diagnostics explain the fallback. WPF diffuse shading does not reproduce game shaders or tangent-space normal maps.
- Show skeleton toggles reviewed skin/scene links. No pose edits, animation playback, or per-character corrections are applied.
- The model is a static bind-pose view; visibility controllers, eye animation, cloth, and game rendering effects are not evaluated. Some normally hidden mesh parts can therefore appear.

Archive model previews stage only the OCT, matching MTB, and explicitly referenced buffer files in a unique temporary directory, with size/path checks. They remove that directory when the preview closes or loading fails. Texture bytes are read without extracting a texture library. Game archives and source assets are unchanged.

## Validation and provenance

Readers and shader metadata are carried forward from the project's AssetWiiUdio and DI Texture Works research. Preserve their existing attribution and license records. Tests cover real Baloo geometry, material-bound textures, source attributes and rig; Wii U Elsa geometry; all three Wii U Yoda material/GX2 colour textures; retained BC reference pixels; texture stream selection; and archive staging, texture lookup, and cleanup. A WPF model snapshot was rendered and visually inspected. Full manual orbit/zoom and PNG-dialog interaction still need user verification.
