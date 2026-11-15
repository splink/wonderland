# Build a Mixed-Reality Miniature Wonderland Prototype

You are the autonomous product designer, technical lead, 3D artist, and Unity engineer for this experiment. **Build a working, installable Meta Quest 3 prototype** from the brief below; do not stop at a proposal, technical spike, mockup, or collection of disconnected demos. Make reasonable engineering and UX decisions independently, iterate through implementation/build/verification, and avoid asking for routine direction. If a true external blocker needs human action (e.g. physically wearing the headset), document it precisely and continue with what you can verify.

## Product vision and visual reference

Read **`reference/final-product-vision.png`** first. It illustrates the eventual product: a magical, colorful, meticulously crafted *miniature landscape growing out of a real living-room floor*, seen through Quest 3 passthrough. Picture lush rolling green countryside, enchanting storybook houses, little forests, and a working model train. The image is **art direction, not a required asset count or scene to reproduce**: its castle, waterfalls, windmill, lakes, extensive village, complex bridges, and dense ornamentation are **out of scope**.

Visual language: whimsical, warm, inviting, carefully art-directed, expressive silhouettes, playful proportions, rich greens, harmonized warm accent colors, tiny handcrafted-looking details and material variation. It should feel like a lovingly crafted storybook diorama, **not** a generic low-poly scene or photorealistic simulation. Geometry may be simple, but the result must **not look visually bare**: invest selectively in surface detail, stylized textures/materials, greenery, variation, lighting, trim and landscaping. Maintain one coherent style across all assets. Train/locomotive height: **about 8 cm in real-world scale**; other assets must be proportionate. Keep the real living room visible through passthrough, and integrate the virtual landscape naturally with the physical floor rather than placing it on an isolated floating table or wooden display pedestal.

## Required five-minute user experience

A new user in a scanned living room should be able to create a charming miniature world using Quest controllers, without editor access:

1. **Terrain:** Start with a small, attractive grassy landscape on/above a patch of real floor (roughly 2 m × 2 m, adjustable to available space). Provide a simple terrain brush to **raise and lower gentle rolling hills**, with a smoothing method/tool so they remain lush rather than spiky. Terrain editing is central to this experiment; do not substitute a permanently fixed landscape. Make brush feedback visible and interaction forgiving. Aim for an appealing ground material, edges, and subtle surface detail.
2. **Forest:** Pick a forest brush and paint clusters of varied miniature trees onto terrain. Brush size and density/strength must be adjustable. Trees should follow the terrain and vary coherently in type, size, rotation, and color. Include a way to erase painted vegetation.
3. **Railway:** Select a track tool and *draw* a curved route over the terrain; generate recognizable rails, sleepers and ballast, not merely a line. Support at least one continuous closed loop; make joining endpoints straightforward. Prefer valid smooth curvature and grades, with clear feedback for invalid input. No switches, bridges, tunnels or complex networks required. Terrain/track interactions should be robust; use a reasonable rule for editing terrain under existing track and explain it briefly.
4. **Train:** Choose one charming, proportionally scaled locomotive (optionally with one or two carriages), place it on the track, and run it smoothly around the loop. Correct orientation, continuous movement and visual coherence matter more than detailed train physics. A simple start/stop control is enough.
5. **Buildings:** Place, rotate and reposition two or three charming house variants. They should sit properly on the terrain. Add an eraser and at least basic undo. Provide save/load for one local world so a session can be resumed.

Interaction philosophy: **paint, draw, place**. A compact in-headset palette is enough. Use simple controller ray/pointer interactions, readable UI, clear previews/selection states and minimal setup. Controller support is required; hand tracking is optional. The goal is to make a compelling little world quickly, not to recreate all the complexity of a model railroad.

## Technical and asset constraints

- Target: **Meta Quest 3 standalone**, Unity 6, existing Meta XR/OpenXR setup where available; passthrough must work on device. Use the available scene/floor information where possible, with a clear fallback when room setup is unavailable. No PC rendering/streaming dependency.
- **No paid 3D-generation services, paid APIs or purchased assets.** Do not depend on heavyweight local NVIDIA-only generative 3D models (development machine has no powerful NVIDIA GPU). Avoid a collage of unrelated downloadable model packs.
- You may use **Blender with Python scripting/headless execution**, Unity procedural meshes and materials/shaders, and appropriately licensed free *code* dependencies. Prefer **small reusable procedural asset generators** (houses, trees, train parts, props) plus consistent material palettes and seeded variation. Choose the actual tools/architecture yourself; Blender MCP is not required.
- Prioritize a small, artistically cohesive scene over asset quantity. Optimize for Quest: sensible draw calls/instancing, lightweight geometry/materials, careful texture sizes, simple collisions and an aspirational **stable 72 FPS on device**, subject to real profiling. Do not claim device performance without measurements.
- Implement actual runtime interaction and generated content; editor-only placement or a beautiful static scene does **not** satisfy the task.

## Autonomy, boundaries and deliverables

Work in a **new standalone Unity project/repository** (or a repository explicitly designated for this experiment); do not modify any unrelated project. Work autonomously, including investigating available tooling, choosing architecture, writing code and asset-generation scripts, creating/providing assets, building, testing and correcting issues. Prioritize one complete end-to-end working user journey over partially building many features. Keep the experiment bounded: **one room; one terrain; a small asset library; a single loop and train**. Do not expand into multiple rooms, water simulation, roads/cars, citizens, multiplayer, elaborate traffic/signals, or a fully procedural city.

Deliver:

1. A Unity project/repository containing source, assets or their reproducible generation scripts, plus all required configuration.
2. **An installable Quest 3 APK** (if the local SDK/signing/build environment permits); provide its exact location and reproducible build commands. If blocked, deliver the furthest verifiable state and the precise blocker.
3. Brief screenshots/rendered previews of generated assets and the assembled scene; clearly distinguish editor renders from in-headset screenshots.
4. A concise report covering what works, what was tested (automatically vs. on a real headset), known defects, scope deviations, performance evidence, and approximate effort/cost. Explicitly assess **(a) visual/art-generation quality, (b) terrain and world-building UX, (c) railway functionality, (d) ability to continue scaling the approach**.

**Acceptance test:** On Quest 3, an untrained user can sculpt a little green hillscape, paint a forest, draw one railway loop, add houses, put a train on the track and watch it run—ideally within five minutes—and the result looks like a charming, coherent miniature world rather than gray primitives. The concept image sets the *direction*, not the quantity of scenery. Be rigorous about what is implemented versus only simulated or unverified.

Begin by inspecting the available project, tools, and the reference image; then proceed directly to execution.
