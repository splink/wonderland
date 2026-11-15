# Wonderland: prototype report

**Status:** the complete five-minute journey is implemented, runs end to end in automated tests, and builds into an
installable Quest 3 APK. **It has not been run on a headset yet**: no Quest was connected to the build machine
(`metavr device list` → "No devices connected"). Nothing below claims device behaviour or device performance.

## Deliverables

| | Where |
|---|---|
| Unity project (Unity 6000.6.4f1, URP 17.6, Meta XR SDK 207, OpenXR 1.18, MRUK 207) | this repository |
| APK (ARM64, IL2CPP, Vulkan, debug-key signed, non-development) | `Builds/Android/Wonderland.apk`: see [Build](#build) |
| Build / test / render commands | `scripts/unity.sh` (see [README](../README.md)) |
| Editor renders (**not** headset captures) | `docs/renders/editor-*.png` |
| Automated test reports | `docs/test-results.txt`, `docs/interaction-test-results.txt` |

There are no headset screenshots, because no device was available.

## What works (and how it was verified)

| Journey step | Implemented | Verified automatically | On headset |
|---|---|---|---|
| Passthrough + floor: world grows out of the real floor | MRUK floor anchor + free-space fit (2.0 → 1.0 m square in front of you); fallback to the guardian floor level (floor-level tracking origin) with an on-screen note; **Move** re-places it (point at the floor, stick rotates) | Scene/config validation (passthrough on, transparent clear, one passthrough layer, USE_SCENE + PASSTHROUGH in the manifest) | ✗ not run |
| 1. Terrain: raise / lower / smooth / flatten, adjustable size and strength, visible brush | ✓ glowing brush ring drawn by the terrain shader; bell falloff; raise/lower also relax; **talus slumping** keeps hills rolling instead of spiky; rim taper into the floor | Journey: hills 8 → 22 cm, steepest slope 1.12 (limit 1.3), never below the floor. Interaction: trigger + aim raises a hill; stick resizes; A cycles mode | ✗ |
| 2. Forest: paint and erase clusters, adjustable size and density | ✓ 5 species × 3 variants; species chosen by a coherent "biome" noise (spruce on high ground, oak/blossom meadows, cypress accents, bush undergrowth); random size, rotation and tint; follows the terrain; chunked meshes | Journey: 146 trees, 5 species; eraser removes trees. Interaction: painting via tile + trigger | ✗ |
| 3. Railway: draw a curved route, closed loop, rails + sleepers + ballast | ✓ auto-closes when you finish within 30 cm of the start (the start ring lights up); smoothing to a minimum curve radius of 18 cm; 4.5 % max grade; rejects self-crossings and track off the landscape with a message and a red preview | Journey: a wobbly, unclosed hand-drawn loop is accepted, closed, min radius 20 cm, max grade 4.5 %; figure-8 and off-edge strokes rejected without changing the world. Interaction: drawing with the trigger lays a closed loop | ✗ |
| Terrain under the track | **Rule: the track owns its corridor.** Laying a track grades the ground to the track's smooth profile (embankments/cuttings) and locks a 12 cm corridor; brushes skip locked ground, so sculpting can never bury or float the rails. Removing the track unlocks it | Journey: bed matches the track within 4 mm; raising across the line moved the bed 0.0 mm | ✗ |
| 4. Train: place, start/stop, runs smoothly | ✓ 8 cm steam locomotive + 2 coaches, each car posed from two bogie points (turns into curves, tilts on grades); spinning wheels, animated side rods, steam puffs with speed; eases to a stop; reverses at buffers on open lines | Journey: 1.44 m in 12 s, worst offset from the rails 4.2 mm, worst alignment 0.999, stops smoothly. Interaction: trigger on the track places it, Start runs it | ✗ |
| 5. Houses: 3 variants, place / rotate / move, sit on terrain | ✓ Cottage, Townhouse, Thatched; ghost preview tinted green/red with reasons ("Too steep here - flatten it first", "That's the railway", "Too close to another house", edge); stick rotates; trigger or grip on a house picks it up; stone plinth sinks into slopes; houses re-sit after sculpting | Journey: all three placed (one after flattening), overlap and on-track refused, ray picking, move + rotate. Interaction: placement, rotation, variant chips | ✗ |
| Eraser, undo, save/load | ✓ eraser taps remove a house, the train or the railway, and holding it clears trees; undo restores whole-world snapshots (30 deep); Save/Load to one local JSON file, plus autosave on pause/quit and restore at launch | Journey: undo; save/load round trip preserves terrain exactly, trees, houses, railway and the running train (190 KB). Interaction: B undo, palette Undo, eraser tap | ✗ |
| Palette UI | ✓ panel on the left controller: six tool tiles with live 3D models of what they make, a contextual options row, a hint line, a status line, and Undo · Save · Load · Move · New (New needs a second press to confirm) | Interaction test clicks tiles, chips and buttons by ray; layout checked in `editor-palette.png` | ✗ |

**Automated tests** (Unity batch mode on this Linux machine):
- `scripts/unity.sh test` runs **34/34 checks PASS**. It is the scripted journey against the world API that the controllers drive.
- `scripts/unity.sh interaction` runs **18/18 PASS**. It drives the real `ToolController` and `PaletteUI` with synthetic controller rays and buttons (UI hit-testing, tool switching, strokes, track drawing, placement, undo, eraser).

What the tests do **not** cover: OVRInput and tracking, passthrough compositing, MRUK on a real room, the permission
prompt, comfort, legibility at real viewing distances, and frame rate.

## Visual / art-generation quality (assessment a)

Every asset is generated at runtime by C#, with seeded generators sharing one palette (`Pal`) and one vertex-coloured
storybook shader (wrapped soft key light, cool-tinted shadows, hemispheric fill, rim light, emissive windows). There are
no textures except the terrain's procedural shading. That shading layers grass greens, sunny meadows, slope and height
tints, cavity darkening, tiny flowers, trodden soil along the railway, and a noisy rim that fades into the passthrough
floor through framebuffer alpha.

- **Strong:** the houses (timber framing, framed glowing windows with shutters and flower boxes, tiled roofs, chimneys,
  stone plinths), the locomotive and coaches, the track (ballast, irregular sleepers, rails with bright running
  surfaces), and the overall coherence. Everything clearly belongs to one world. Ground cover (about 1,300 tufts,
  flower clumps and pebbles) keeps open meadow from looking bare.
- **Weaker:** the trees read as clean "clay" toy trees rather than the richly detailed foliage of the concept art. The
  thatched roof is cute but blobby. There is no ambient occlusion beyond vertex shading, no bloom or warm evening light,
  and no decor (lamps, fences, sheep). The result is a charming toy diorama, a clear step below the concept image's
  painterly density, as the brief anticipated.
- **Unverified:** colours and contrast through Quest passthrough (the warm wooden floor in the renders is a procedural
  stand-in), and whether the 2-3 mm details (rails, window frames) survive at headset resolution. Render scale is 1.0
  with 4× MSAA for that reason.

## Terrain and world-building UX (assessment b)

The interaction model is **paint, draw, place**: one palette, one pointer, the trigger for everything, the stick for
size/strength (or rotation), A for mode, B for undo. There is feedback everywhere: a coloured brush ring per mode, a
growing ring while stroking, haptic ticks while sculpting or painting, a green start ring when a stroke would close the
loop, a red preview plus a message when a track is invalid, ghost houses tinted by validity with a reason, and
hover/selection states on the palette.

The journey is forgiving by design. Hand jitter is smoothed out, loops close automatically, tight curves are rounded,
terrain under the track is graded automatically, trees and houses in the way of a new railway are cleared, and almost
everything is one undo away.

Risks that only a headset session can settle:
- the palette's position on the left controller (tilt and offset are guesses)
- reach and aim at floor level (the pointer is pitched 12° down)
- brush sensitivity
- text size, since the panel text is about 6-9 mm tall at roughly 40 cm

## Railway functionality (assessment c)

The planner works like this:
1. Resample the stroke at 3 cm.
2. Apply Taubin denoising, which removes hand jitter without shrinking the loop.
3. Apply localised Laplacian smoothing only where the curvature radius is under 18 cm.
4. Validate: no self-crossing, the track stays on the landscape, and the loop is at least 1.1 m long.
5. Build a 1 cm Catmull-Rom centre line.
6. Compute a smoothed, grade-limited height profile (≤ 4.5 %).

The terrain is then graded and locked along the line. Open (unclosed) lines are allowed: they get buffer stops and the
train shuttles. There is a single railway; drawing a new one replaces it, and the train moves onto the new line.

Not implemented, as scoped: switches, bridges, tunnels and multiple trains. Also not implemented: extending an
existing line from its end, and steep track. Grades are clamped and the ground is graded to match, so very hilly
strokes produce deep cuttings rather than an error.

## Ability to continue scaling the approach (assessment d)

**Good:**
- The generators are small (about 1,400 lines for all assets), deterministic and stylistically unified, so new
  variants are cheap: a new house is roughly 60 lines of parts.
- The world API is shared by input and tests, which makes regression testing easy (journey + interaction tests run in
  about 1 minute each).
- Chunked meshes keep draw calls bounded.
- The scene and project configuration are generated (`configure`), so the setup is reproducible.

**Limits:**
- Hand-coding geometry gets slow for organic detail (foliage, characters, sheep). Moving further toward the concept's
  richness would likely need Blender-Python generators with baked AO, or hand-authored hero assets.
- The single heightfield (97×97 over 2 m, about 2 cm cells) cannot do overhangs, tunnels or crisp cliffs.
- Undo by full snapshot is fine at this size but wasteful at larger scales.

## Performance evidence

No device measurements exist. Static budget of the scripted test world (115 trees, 3 houses, a 3.4 m loop, train,
ground cover):
- **102 mesh renderers** and **170 k triangles** for the world, excluding UI. With single-pass instanced stereo this
  is roughly 100 draw calls plus the shadow pass.
- One 1024 px shadow map, soft shadows (low), shadow distance 4 m.
- No textures besides fonts, and one URP forward renderer.

Budget choices made blind:
- render scale 1.0 (EasyPiano used 0.8), MSAA 4×, Vulkan
- terrain updates only the touched region
- forest and ground cover rebuild only edited 25 cm chunks
- caps: 1,400 trees and 30 undo snapshots

Whether this holds 72 FPS on Quest 3 is **unknown**. It is plausible for the vertex count, but shadows plus MSAA plus
render scale 1.0 are the first knobs if not. Measure with `metavr` / OVR Metrics, or a development build
(`WONDERLAND_DEVELOPMENT=1 scripts/unity.sh build`).

## Known defects and risks

- **Never run on a device.** Possible issues include: palette orientation on the controller, passthrough alpha at
  the terrain rim (premultiplied output, assumed to composite as intended), MRUK load timing, and permission-prompt
  flow. MRUK `LoadSceneFromDevice` is called without forcing a room scan; with no scan, the app falls back to the
  guardian floor and asks you to use Move.
- The world is placed in front of you at launch. It is not anchored (no spatial anchors), so a restored world
  reappears in front of the player rather than in its previous physical spot.
- The Meta SDK manifest step re-adds `USE_ANCHOR_API` and `HEADSET_CAMERA` declarations that the app does not use.
  They are harmless but not least-privilege.
- Hand tracking is not supported (controllers only, as allowed).
- When the stroke ends far from the start, the railway is open and the train shuttles. That is intended, but a user
  expecting a loop must redraw.
- Trees can be painted right up to houses; houses clear the trees under their footprint only.

## Scope deviations

- No Blender. All generation is Unity C#, which is simpler to keep runtime, reproducible and stylistically unified.
- No Decor tool. It was out of the required scope; ground cover is automatic instead.
- Added beyond the brief: Flatten brush, automatic ground cover, Move/New world, autosave/restore.

## Build

```
scripts/unity.sh build   # -> Builds/Android/Wonderland.apk
metavr app install Builds/Android/Wonderland.apk && metavr app launch com.splink.wonderland
```

Built 2026-11-15: `Builds/Android/Wonderland.apk`, 57.6 MB, sha256 `a35a371fcef4292c…`, package `com.splink.wonderland`
0.1.0 (versionCode 1), minSdk 32 / targetSdk 34. Shader-error check clean. The APK manifest declares USE_SCENE,
USE_ANCHOR_API, BOUNDARY_VISIBILITY, HEADSET_CAMERA and INTERNET (the last added by Unity/Meta SDK defaults).

## Effort and cost

- About 1.5 hours of autonomous agent work from the copied project settings to the APK.
- About 5,900 lines of C#, HLSL and ShaderLab.
- No paid services, APIs or assets; everything was built and rendered locally (Unity batch mode on an AMD laptop GPU).

## Next steps (headset session)

1. Install the APK and run the acceptance journey with an untrained user. Note palette ergonomics, brush feel, and
   whether the start ring makes closing the loop obvious.
2. Capture an OVR Metrics trace at the busiest moment (forest + train + shadows); tune render scale, shadows and
   MSAA if needed.
3. Check the passthrough integration of the terrain rim, floor height accuracy, and the MRUK fallback path.
4. Art pass priorities: richer foliage (leaf-card clusters or baked AO), warm evening light/bloom, a few decor
   props (lamps, fences, sheep).
