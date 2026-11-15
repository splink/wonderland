# Wonderland — a mixed-reality miniature world for Quest 3

Grow a storybook miniature landscape out of your real living-room floor (passthrough), then **paint, draw, place**:
sculpt rolling hills, paint forests, draw a railway loop, put down houses and watch a little steam train run.

Brief: [`MiniatureWonderland_ClaudePrompt.md`](MiniatureWonderland_ClaudePrompt.md) · art direction: [`reference/final-product-vision.png`](reference/final-product-vision.png) · report: [`docs/REPORT.md`](docs/REPORT.md)

## Build and install

Requirements: Unity **6000.6.4f1** with Android support (at `~/Unity/Hub/Editor/6000.6.4f1`, or set `UNITY_EDITOR`),
Meta XR SDK 207 / OpenXR / URP / MRUK (resolved from `Packages/manifest.json`). Close any Editor that has the project open.

```bash
scripts/unity.sh configure   # regenerate Assets/Scenes/Wonderland.unity + Quest settings + manifest (idempotent)
scripts/unity.sh build       # configure + ARM64 IL2CPP APK -> Builds/Android/Wonderland.apk  (WONDERLAND_DEVELOPMENT=1 for a dev build)
scripts/unity.sh test        # scripted five-minute journey against the world API -> docs/test-results.txt
scripts/unity.sh interaction # synthetic-controller test of palette + tools -> docs/interaction-test-results.txt
scripts/unity.sh gallery     # editor renders of the generated assets -> docs/renders/editor-*.png (needs a GPU, no -nographics)
scripts/unity.sh renders     # editor renders of an assembled world built by the scripted journey

metavr device list && metavr app install Builds/Android/Wonderland.apk && metavr app launch com.splink.wonderland
```

The APK is signed with Unity's debug key (sideloading only).

## Controls (Touch controllers)

The palette floats above the **left** controller; the **right** controller points.

| Input | Action |
|---|---|
| Right trigger | use the tool / press palette buttons |
| Right stick ← → | brush size (Houses: rotate) |
| Right stick ↑ ↓ | strength (Forest: density) |
| A | next mode: Raise → Lower → Smooth → Flatten → Grow → Trim · Paint ↔ Erase · next house · train start/stop |
| B | undo |
| Right grip | pick up a house to move it |
| Left Y | hide / show the palette |

**Growing the world:** it starts as a small patch of land (about 1 m²) in front of you. In the Terrain tool pick
**Grow** and paint over the real floor: new land rises out of it with the same soft, organic edge, anywhere on the free
floor of your room (from the Quest room scan: not through walls, not under furniture; without a scan the guardian area or
a 4.8 m square is used). **Trim** takes land away (never under the railway or houses).

Palette: **Terrain · Forest · Track · Train · Houses · Eraser** tools, an options row for the current tool, and
**Undo · Save · Load · Move · New**. The world saves automatically when the app is paused or closed and is restored
on the next launch.

## How it is built

Everything you see is generated at runtime by C# from seeded procedural generators sharing one palette and two
shaders: no downloaded or purchased models, textures or services.

| Area | Code |
|---|---|
| Mesh toolkit, noise, palette | `Assets/Wonderland/Scripts/Core/` |
| Trees (5 species × 3 variants), houses (3), locomotive + 2 coaches | `Assets/Wonderland/Scripts/Assets/` |
| Terrain field + renderer, forest, railway planner + mesh, buildings, train, world (undo / save) | `Assets/Wonderland/Scripts/World/` |
| Palette UI, controller input, tools | `Assets/Wonderland/Scripts/Interaction/` |
| Boot: permission, MRUK floor + free space, placement, autosave | `Assets/Wonderland/Scripts/App/WonderlandApp.cs` |
| Shaders: `Wonderland/Lit` (vertex-coloured storybook shading), `Wonderland/Terrain` (procedural grass, flowers, soil, rim fade, brush ring), UI, steam | `Assets/Wonderland/Shaders/` |
| Scene + Quest configuration, build, tests, renders | `Assets/Wonderland/Editor/` |

The project settings were seeded from the EasyPiano Quest project (same Unity / Meta XR / URP stack); the
application is `com.splink.wonderland`.
