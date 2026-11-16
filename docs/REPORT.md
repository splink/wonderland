# Wonderland: prototype report

**Status:** the complete five-minute journey is implemented, runs end to end in automated tests, and builds into an
installable Quest 3 APK. Earlier builds were installed on a Quest 3 and tried by the user; their feedback drove the
later rounds: growing the land freely, a storybook art pass, forgiving railway loops, and then house pads, a railway that
follows the land, a station, water, bridges, tunnels, animals and villagers. **No frame-timing measurements exist
yet.** The latest APK is installed, but the headset was idle (the app was stopped on launch), so nothing below claims
device performance.

## Deliverables

| | Where |
|---|---|
| Unity project (Unity 6000.6.4f1, URP 17.6, Meta XR SDK 207, OpenXR 1.18, MRUK 207) | this repository |
| APK (ARM64, IL2CPP, Vulkan, debug-key signed, non-development) | `Builds/Android/Wonderland.apk`: see [Build](#build) |
| Build / test / render commands | `scripts/unity.sh` (see [README](../README.md)) |
| Editor renders (**not** headset captures) | `docs/renders/editor-*.png` |
| Automated test reports | `docs/test-results.txt`, `docs/interaction-test-results.txt` |

There are no headset screenshots; all images are editor renders.

## What works (and how it was verified)

| Journey step | Implemented | Verified automatically | On headset |
|---|---|---|---|
| Passthrough + floor: world grows out of the real floor | MRUK floor anchor + free-space fit (2.0 → 1.0 m square in front of you); fallback to the guardian floor level (floor-level tracking origin) with an on-screen note; **Move** re-places it (point at the floor, stick rotates) | Scene/config validation (passthrough on, transparent clear, one passthrough layer, USE_SCENE + PASSTHROUGH in the manifest) | ✗ not run |
| 1. Terrain: raise / lower / smooth / flatten, adjustable size and strength, visible brush | ✓ glowing brush ring drawn by the terrain shader; bell falloff; raise/lower also relax; **talus slumping** keeps hills rolling instead of spiky; rim taper into the floor | Journey: hills 8 → 22 cm, steepest slope 1.12 (limit 1.3), never below the floor. Interaction: trigger + aim raises a hill; stick resizes; A cycles mode | ✗ |
| 2. Forest: paint and erase clusters, adjustable size and density | ✓ 5 species × 3 variants; species chosen by a coherent "biome" noise (spruce on high ground, oak/blossom meadows, cypress accents, bush undergrowth); random size, rotation and tint; follows the terrain; chunked meshes | Journey: 146 trees, 5 species; eraser removes trees. Interaction: painting via tile + trigger | ✗ |
| 3. Railway: draw a curved route, closed loop, rails + sleepers + ballast | ✓ auto-closes when you finish within 30 cm of the start (the start ring lights up); smoothing to a minimum curve radius of 18 cm; 9 % max grade; rejects self-crossings with a message and a red preview | Journey: a wobbly, unclosed hand-drawn loop is accepted, closed, min radius 20 cm, figure-8 rejected without changing the world. Interaction: drawing with the trigger lays a closed loop | ✗ |
| Terrain under the track | **Rule: the track owns its corridor.** Laying a track grades the ground to the track's smooth profile (embankments/cuttings) and locks a 12 cm corridor; brushes skip locked ground, so sculpting can never bury or float the rails. Removing the track unlocks it | Journey: bed matches the track within 4 mm; raising across the line moved the bed 0.0 mm | ✗ |
| 4. Train: place, start/stop, runs smoothly | ✓ 8 cm steam locomotive + 2 coaches, each car posed from two bogie points (turns into curves, tilts on grades); spinning wheels, animated side rods, steam puffs with speed; eases to a stop; reverses at buffers on open lines | Journey: 1.44 m in 12 s, worst offset from the rails 4.2 mm, worst alignment 0.999, stops smoothly. Interaction: trigger on the track places it, Start runs it | ✗ |
| 5. Houses: 3 variants, place / rotate / move, sit on terrain | ✓ Cottage, Townhouse, Thatched; ghost preview tinted green/red with reasons ("Too steep here - flatten it first", "That's the railway", "Too close to another house", edge); stick rotates; trigger or grip on a house picks it up; stone plinth sinks into slopes; houses re-sit after sculpting | Journey: all three placed (one after flattening), overlap and on-track refused, ray picking, move + rotate. Interaction: placement, rotation, variant chips | ✗ |
| Grow the world freely (added after the headset sessions) | ✓ The world starts as a ~1 m² patch; **Grow / Trim** paint land onto or off the real floor anywhere. The terrain grid (2 cm cells) grows on demand in 1.2 m steps, up to 16 × 16 m around the start (memory backstop); the room scan only sizes the initial grid and never fences growth. (An earlier version limited growth to the scanned free floor; the scanned room mesh's room-sized volume was mistaken for furniture and blocked everything, so room fencing was removed.) The rim comes from the land mask; Trim spares the railway and houses and drops slivers; undo depth adapts to the grid size (48 MB budget) | Journey: grow, plant, trim, land save/load, v1 migration, land grows past a scanned room and the grid grows 4.8 → 7.2 m, bad room data cannot block growth, trimmed land grows back. Interaction: Grow via the palette | ✗ |
| House pads | ✓ A placed house cuts and fills a flat pad under its footprint (blended into the slope) that brushes leave alone, so a house on lifted ground sits flush instead of being half buried | Journey: worst footprint gap 0.0 mm; a 5.3 cm slope becomes a flat pad that stays flat while brushing next to it | ✗ |
| Railway follows the land | ✓ Sculpting under the track lifts or lowers it (throttled re-plan, 4 times a second): the grade-limited profile is recomputed from the new ground (max grade 9 %) and the bed regraded. The train swaps onto the new layout seamlessly | Journey: raising under the line lifts the track 7.7 → 10.1 cm, bed regraded, grade ≤ 9.1 % | ✗ |
| Railway station | ✓ 4th house type: clock tower, canopy, benches. It snaps beside the nearest rails, facing them; a curved platform is generated along the rails; the train eases to a stop centred on it, waits 3.5 s and pulls away | Journey: snaps, platform built, halts centred (≤ 3 cm), pulls away, saved | ✗ |
| Ponds and lakes | ✓ Water tool, Pond mode: digs a basin with soft levee banks and fills it with a flat water surface at one level (start inside a pond to extend it); Drain removes water. Water shader: depth tint, ripples, glints, shore foam. Trees, houses and animals keep out; reeds and lily pads grow at the edge | Journey: basin 1.6 cm level / 0.3 cm bed, one level when extended, trees and houses refused, drain, saved | ✗ |
| Streams and bridges | ✓ Stream mode: drawn like the railway; the water level only falls downstream (it cuts through rises instead of flowing uphill), carves a channel, and shows flow and white water where it drops. Where the railway crosses water (pond or stream) its profile rises to clear it and an arched stone bridge with piers, voussoirs and parapets is generated | Journey: stream carves below its water, no trees in it, railway bridges it (58 mm clearance), railway across a pond becomes a bridge and the water stays | ✗ |
| Tunnels | ✓ Where the ground over the line stands more than 10 cm (taller than the train) above the bed, that stretch becomes a tunnel: the hill is kept, a short cutting is dug to each mouth, and stone portals with a dark arch are generated (the classic model-railway trick: the train vanishes into the dark). Steam stops underground | Journey: a loop under a 29 cm hill gets 30 cm of tunnel with the hill kept; the train runs through with steam off | ✗ |
| Sheep and cows | ✓ Decor tool: tap, or hold and sweep for a herd. Each animal grazes head-down, picks a spot of free grass within 22 cm of home, turns, ambles over with a bob, and grazes again; it keeps off water, rails and houses | Journey: herd placed, refused in water and on the railway, wanders, stays on free grass, eraser, saved | ✗ |
| Villagers | ✓ Decor tool, Villagers: 3 cm figures in 6 outfits (straw hats, flat caps, bonnets) with swinging arms and legs. They visit neighbours' doorsteps and gesture as they chat, look around quiet spots, walk to the station and wait on the platform, board when the train stops, ride, and get off at the next stop (and live near there from then on). Whenever the train passes within about 24 cm, they turn and wave. They keep to dry land, off the rails and out of houses, steering around obstacles; they move house if a pond or building covers theirs | Journey (4 simulated minutes): stroll, call at a door, wait on the platform, board, wave, never on rails / in water / in a house, eraser, saved | ✗ |
| Railway network | ✓ Extend, branch, join, cross (diamond), loop, bend by grip, erase one piece; up to 8 trains (steam, goods with 4 wagons, railcar) in 6 liveries; automatic reservations, switch and stop signals, Explore / Random / Manual switches, per-train speed, lift and re-place trains | Journey: figure 8 crossing; branch to a dead end; extend; join; Manual and Explore switches; three trains for 3 minutes never closer than 5.5 cm between cars and none jammed; two crossings shared; head-on resolution; red / green signal; pinned switch; bend; erase + undo; save/load. Interaction: drawing a branch and tapping its switch with the controller, placing a running train, Stop all / Start all | ✗ |
| Object library | ✓ 41 places in 7 categories (homes, town, farm, fun, travel, nature, small details), chosen in two rows of chips (category, then item). Turning parts: the carousel, the windmill's sails, and the Ferris wheel with upright cabins. Small details may stand close to buildings; rocks and camps follow the ground instead of getting a pad | Journey: every object builds (largest 16.5 k vertices); a village square goes up. Interaction: category and item chips place a fountain | ✗ |
| Paths, fences, pastures | ✓ One drawing gesture for roads, lanes, footpaths, trails, fences, hedges and walls. Ends snap onto lines of the same family; roads and lanes grade the ground; closed barriers make enclosures; ways crossing the railway get level crossings. A walking graph with A* gives villagers routes | Journey: a road smooths the ground across it, a path joins it, a route is found, no houses on the road but lamps beside it; a fenced pasture keeps its sheep in and the horse outside out; a level crossing lets villagers cross the rails there and only there; undo and save/load | ✗ |
| Living world | ✓ Places bring activity spots (door, seat, gathering point, play, ride, view, browse). Villagers pick free spots near home, weighted, with fun places drawing people from further away; claims stop two people taking one chair. Riders sit on the carousel's horses as it turns, or vanish into a Ferris wheel cabin. Homes bring residents. Hikers walk the trails. Animals keep to habitats: pasture grazers, hens, ducks on ponds, deer by the woods that flee | Journey: residents move in; villagers sit, gather and browse; visitors ride and play at the fair; a hiker sets off; a duck stays on the pond; deer refuse open ground, settle by the wood and bolt from a villager | ✗ |
| Mountains and beaches | ✓ Peaks brush (ridged noise); the terrain shader adds alpine scree, layered rock bands on cliffs, snow on the tops and sand along pond shores; ground cover adds boulders and alpine flowers | Journey: the Peaks brush raises a craggy mountain | ✗ |
| Bridges | ✓ Derived from what crosses water (railway pieces and ways): crossings close together and parallel share one deck; 7 styles (stone arches, timber trestle, steel truss, suspension, covered, footbridge, stepping stones) offered by what the bridge carries and its span; a tap in the Track or Paths tool cycles them; choices are remembered by place (saved, undoable); ways rise onto the deck and villagers walk across | Journey: rail bridge over a pond; a parallel second track shares and widens it; tap restyles; survives rebuild, save/load, undo; road bridge deck 26 mm above the water and walkable; stepping stones for a trail, a footbridge for a footpath; suspension only for long spans | ✗ |
| Eraser, undo, save/load | ✓ eraser taps remove a house, a stream, an animal, a villager, the train or the railway, and holding it clears trees; undo restores whole-world snapshots (depth adapts to a 48 MB budget); Save/Load to one local JSON file, plus autosave on pause/quit and restore at launch | Journey: undo; save/load round trip preserves terrain exactly, trees, houses, railway and the running train (190 KB). Interaction: B undo, palette Undo, eraser tap | ✗ |
| Palette UI | ✓ panel on the left controller: eight tool tiles with live 3D models of what they make, a contextual options row, a hint line, a status line, and Undo · Save · Load · Move · New (New needs a second press to confirm) | Interaction test clicks tiles, chips and buttons by ray; layout checked in `editor-palette.png` | ✗ |

**Automated tests** (Unity batch mode on this Linux machine):
- `scripts/unity.sh test` runs **182/182 checks PASS**. It is the scripted journey against the world API that the controllers drive.
- `scripts/unity.sh interaction` runs **28/28 PASS**. It drives the real `ToolController` and `PaletteUI` with synthetic controller rays and buttons (UI hit-testing, tool switching, strokes, track drawing, placement, undo, eraser).

What the tests do **not** cover: OVRInput and tracking, passthrough compositing, MRUK on a real room, the permission
prompt, comfort, legibility at real viewing distances, and frame rate.

## Visual / art-generation quality (assessment a)

Every asset is generated at runtime by C#, with seeded generators sharing one palette (`Pal`) and one vertex-coloured
storybook shader (wrapped soft key light, cool-tinted shadows, hemispheric fill, rim light, emissive windows). There are
no textures except the terrain's procedural shading. That shading layers grass greens, sunny meadows, slope and height
tints, cavity darkening, tiny flowers, trodden soil along the railway, and a noisy rim that fades into the passthrough
floor through framebuffer alpha.

**Storybook art pass** (after the first headset session; "more wonder per object", construction UX unchanged):
- **Houses:** swayback tiled roofs with deep, lifted eaves; scalloped bargeboards, finials and crested ridges; dormers
  with their own little roofs; crooked chimneys with pots; a jettied upper storey on carved brackets (townhouse); a
  stepped, fat thatch with eyebrows over round windows; windows with curtains, heart-cut shutters, arched heads and
  flower boxes with trailing ivy; door hood, lanterns, a hanging shop sign, a rooster weather vane; climbing roses,
  stepping stones and shrubs; a hand-made warp (lean, twist, sagging ridge). Each of the 3 types comes in **4 colour
  styles**, and successive placements cycle through them, so a village never repeats itself.
- **Vegetation:** crowns are built from smooth multi-lobed leaf clusters in tiers, with lobe highlights, crevice shadow
  and painted leaf dabs. Trunks are gnarled, with root flares, branches and knot holes. Species are now: storybook oak,
  linden, golden oak (accent), apple tree, cherry, white cherry, weeping cherry, three tiered spruces with cones and
  crooked leaders, flame cypress, poplar, leafy, pink and yellow flowering bushes, and a fern clump. Each tree stands in
  undergrowth (grass, ferns, flowers, the odd toadstool).
- **Meadow:** drifts of tulips, daisies and lavender, flower clumps, pebbles and rare toadstool rings.
- **Wind:** foliage, grass and flowers sway gently, computed in the shared vertex shader (and its shadow pass).

- **Life:** woolly sheep and patched cows that nod while grazing; villagers with rosy cheeks, hats and bonnets
  (`editor-villagers.png`); a station with a clock tower and canopy; stone bridges and tunnel portals in the same
  palette; water with ripples, glints and foam.

- **Weaker:** no ambient occlusion beyond vertex shading, no bloom or warm evening light, and no small props (lamps,
  fences). Riding villagers are hidden rather than shown at the coach windows. Foliage is smooth "clay" lobes rather than individual leaves. Still a step below the concept image's
  painterly density, but much closer in spirit.
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

The railway is a **network**: a graph of track pieces joined by three-way switches, with diamond crossings where pieces
cross. Construction never replaces anything, and every stroke is one of a few cases:
- **Extend:** a stroke that starts or ends at a dead end extends that line.
- **Branch or join:** a stroke that starts or ends on a piece splits it and adds a switch.
- **Cross:** a stroke that crosses track gets a diamond crossing, at a shared height. It must cross at 22° or more and
  stay clear of switches.
- **Loop:** a free stroke that comes back to its start becomes a loop. Overshooting or stopping short (up to 30 cm)
  both close, and small accidental loops are snipped out.

How the planner shapes the new track:
- At a switch, the new track starts with a 4 cm straight in the old track's direction, then an arc of at least 20 cm
  radius that turns until it heads for the drawn line.
- That lead stays fixed while the rest of the stroke is smoothed to the 18 cm minimum radius, so the joint never kinks.
- New track squeezed closer than 8.8 cm alongside other track is refused.
- The network is then re-profiled: every switch and crossing gets one height from the ground, shared by all the pieces
  meeting there, and each piece gets a grade-limited profile (≤ 9 %) between them.
- After that come bridges over water, tunnels under tall ground, and grading of the corridors.

Editing:
- Grip-dragging bends one piece like a rope; its ends stay put at switches. The result is rounded and checked against
  the rest of the network before it is applied.
- The eraser removes one piece; switches left with two ends merge their pieces back into one.

**Operations.** Each train stands on a path of track spans and moves by growing it at the leading end.

Safety is automatic:
- Before moving, a train reserves its braking distance plus 6 cm of track ahead.
- It never reserves track another train stands on or has reserved, and keeps 3 cm between trains.
- It takes a switch (to 16 cm on every branch) or a crossing as a whole, so it never stops inside one.

Switches:
- Trains arriving from either branch always pass through onto the trunk.
- Trains arriving from the trunk follow the switch's setting, which the global mode controls: Explore alternates
  after each train, Random picks, Manual leaves it as set.
- A switch tapped by hand is pinned until it is tapped again.

Stopping and reversing:
- Trains stop centred on station platforms.
- They wait at red stop signals, and the automatic signals at every switch show which train holds it.
- They reverse at buffers (a steam train pushes its coaches).
- Two trains facing each other on single track resolve it by one backing away; a train stuck behind another for
  12 s turns back too, unless the one ahead is at a signal or a station.

Not implemented: flyovers (crossings are at grade only), a line bridging a dry valley, couplings and shunting, and
sending a train to a chosen destination (routing is local, by switch settings).

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
- The single heightfield (2 cm cells, grown on demand) cannot do overhangs or crisp cliffs. Tunnels work around it:
  the hill stays a heightfield and the portal's dark arch hides the cut, which is convincing from outside but
  has no interior to look into. On a large grid, undo, load and re-fit recompute the whole rim (a brief hitch); worth measuring on device.
- Undo by full snapshot (float heights + byte masks: about 1.3 MB for a 4.6 × 3.6 m room, 20 deep) is fine here but
  wasteful at larger scales.

## Performance evidence

The user reported the earlier build "felt a bit strained". I did not guess; the likely cause was CPU stutter from
whole-chunk mesh rebuilds during strokes, so that is what was changed:
- **Throttled, budgeted rebuilds:** terrain (60 × 60-cell chunks), forest and ground cover (25 cm buckets) rebuild only
  dirty chunks, a few per frame, reusing builders; trees append with a fast rotation-only path. Re-planning the railway
  after sculpting runs at most 4 times a second, and streams at most 3 times a second.
- **GPU:** fixed foveated rendering High with dynamic foveation; ground cover casts no shadows; tree cap 450.
- **Measurement hook:** `PerfLog` writes a `Wonderland perf` line every 5 s (FrameTimingManager CPU and GPU frame
  times, worst frame). Read it with `metavr log --tag Unity | grep "Wonderland perf"`.

**Not yet measured:** no `Wonderland perf` lines or Perfetto traces exist. The headset was idle during this session,
so the app stopped on launch.

Static budget (`docs/renders/asset-budget.txt`):
- trees: 0.4–2.5 k vertices
- houses: 7–14 k vertices (station 7.5 k)
- villager: 642 vertices (5 parts)
- sheep: 828 vertices; cow: 574 vertices

Each animal or villager is 2–5 renderers, so 30 villagers add about 150 draw calls (SRP batcher, one material). If that
shows up in a trace, the next step is GPU instancing, or merging the body into one skinned mesh. Render scale 1.0,
MSAA 4× and one 1024 px shadow map remain the first knobs on the GPU side.

## Known defects and risks

- **Station, water, bridges, tunnels, animals and villagers are untested on the device.** They are covered by the
  automated journey and editor renders only.
- Villagers cannot cross the railway (there are no level crossings); someone living on the inside of the loop stays
  inside it unless they take the train. Their steering is local (no path planning), so they give up on a walk if a
  house is in the way at an awkward angle.
- Streams don't merge or branch, and a stream ends where it was drawn (no lake outflow). The railway bridges only
  water, not dry valleys.
- Tunnel portals are placed from the railway alone. If you sculpt a hill over the line afterwards the tunnel appears
  on the next re-plan, but very lumpy hills can give short tunnels that are dropped (under 8 cm) and become cuttings.
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
- Added beyond the brief: Flatten brush, automatic ground cover, Move/New world, autosave/restore, free land growth,
  water (ponds, streams), bridges, tunnels, the station, a Decor tool with sheep, cows and villagers, and a railway
  network with switches, crossings, signals and several kinds of train.

## Build

```
scripts/unity.sh build   # -> Builds/Android/Wonderland.apk
metavr app install Builds/Android/Wonderland.apk && metavr app launch com.splink.wonderland
```

Built 2026-11-16: `Builds/Android/Wonderland.apk`, 58.2 MB, sha256 `f2590a0c802538ef…`, package `com.splink.wonderland`
0.1.0 (versionCode 1), minSdk 32 / targetSdk 34. Shader-error check clean. The APK manifest declares USE_SCENE,
USE_ANCHOR_API, BOUNDARY_VISIBILITY, HEADSET_CAMERA and INTERNET (the last added by Unity/Meta SDK defaults).

## Effort and cost

- About 1.5 hours of autonomous agent work from the copied project settings to the APK.
- About 10,300 lines of C#, HLSL and ShaderLab.
- No paid services, APIs or assets; everything was built and rendered locally (Unity batch mode on an AMD laptop GPU).

## Next steps (headset session)

1. Wear the headset with the app running for a minute of heavy use (sculpting next to a forest with the train and
   villagers out), then read the `Wonderland perf` lines, or capture a trace with `metavr perf capture`.
2. Try the new tools: dig a pond and draw a stream, and run the railway over them; raise a big hill across the line for
   a tunnel; place a station and a few villagers next to it, and watch them board.
3. Check legibility of the 3 cm villagers and the water's look through passthrough.
4. If draw calls show up: instance the animals and villagers.
