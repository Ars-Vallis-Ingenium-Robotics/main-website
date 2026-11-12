# 3D models (ROSIE and ADAM)

The SMS pages show the competition CAD in a self-hosted `<model-viewer>` (v4.4.0, Apache-2.0,
`wwwroot/lib/model-viewer/`). Pages load it only when a `<model-viewer>` is on the page (`js/media.js`),
and the model itself only downloads when the visitor presses "Spin it in 3D".

Sources (SMS Design shared drive, `CAD - Sequoia Marine 2026`):
- ROSIE: `01 - Chassis CAD/2603_ROV_03.05.25.02.glb` (Draco-compressed SolidWorks export, 4.7M triangles)
- ADAM: `03 - Float CAD/2603_Float_03.05.17.26.STEP`

How the web copies in `wwwroot/models/` were made:
1. ADAM: STEP → GLB with `cascadio` (`pip install cascadio`), then
   `gltf-transform optimize adam_src.glb adam.glb --compress false --palette false --simplify-ratio 0.12`.
2. ROSIE: `gltf-transform copy` (decodes Draco) → dedup/flatten/join/weld, then `simplify_rosie.py r1.glb out.glb 450000`
   (drops sliver components under 4 mm and decimates each part with `fast-simplification`).
3. `node recolor.mjs` sets the real colours (black PETG, SMS teal accents, silver ADAM tube).
4. `gltf-transform quantize` to shrink the files (ROSIE ≈ 6 MB, ADAM ≈ 1.3 MB).
5. Posters (`*-poster.webp`) are screenshots of the same viewer with a transparent background.

CSP note: model-viewer instantiates WebAssembly, so `script-src` includes `'wasm-unsafe-eval'`
(WebAssembly only; it does not allow JS `eval`).
