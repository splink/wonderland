# Wonderland — a mixed-reality miniature world for Quest 3

Grow a storybook miniature landscape out of your real living-room floor (passthrough), then **paint, draw, place**:
sculpt rolling hills, dig ponds and draw streams, paint forests, draw a railway loop (it bridges water and tunnels
through hills), put down houses and a station, let sheep, cows and villagers loose, and watch a little steam train run.

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
| A | next mode: Raise → Lower → Smooth → Flatten → Grow → Trim → Peaks · next path kind · next item · Pond → Stream → Drain · Paint ↔ Erase · next house · Sheep → Cows → Villagers · Track: draw ↔ signals · Trains: start/stop (the one pointed at, or all) |
| B | undo |
| Right grip | pick up a house, lift a train, or bend a piece of track |
| Left Y | hide / show the palette |

**Growing the world:** it starts as a small patch of land (about 1 m²) in front of you. In the Terrain tool pick
**Grow** and paint over the real floor: new land rises out of it with the same soft, organic edge, wherever you like.
The terrain grows to meet you, up to 8 m from where the world started (a memory backstop); the room scan only gives it a
head start. **Trim** takes land away (never under the railway or houses).

**Railway network (Track tool: build).** Everything you draw connects:
- start or end a stroke at a **dead end** to extend that line
- start on a **piece of track** to branch off with a new switch; end on track to join it
- a stroke that **crosses** track gets a diamond crossing (a figure 8 is fine now)
- a free stroke that comes back to its start becomes a **loop**, even if you overshoot or stop a little short; drawn
  over bare floor, land grows under it

The planner bends new track so it leaves and meets the old one smoothly, like a real switch, and rounds tight curves.
Adding never replaces anything. **Grip** a piece of track to bend it like a rope (its switches stay put), the
**eraser** removes one piece (from switch to switch), and **Signal** mode puts up stop signals. The track follows the
land when you sculpt under it (up to a 9 % grade), rises onto stone bridges over water, and runs in tunnels under hills
taller than the train.

**Trains (Train tool: play).** Tap the track to add a steam train, a goods train or a railcar; each gets its own
colours and starts running. Tap a train to stop or start it, push the stick up/down to change its speed, grip to lift it
onto other track. Tap a switch to throw it (it stays set until you tap it again), tap a stop signal to turn it red or
green. Safety is automatic: trains reserve the track ahead of them and never share a switch or crossing, so they never
collide; they wait at red signals, stop at stations, reverse at buffers, and back away when they meet head-on. The
**Switches** option sets how switches behave when nobody has set them: **Explore** (they alternate, so trains roam the
whole network), **Random**, or **Manual**.

**Water:** in the Water tool, **Pond** digs a basin that fills with water (start inside a pond to extend it at the same
level), **Stream** is drawn like the railway and flows only downhill, carving a channel, and **Drain** takes water
away (tap a stream to remove it). Reeds and lily pads grow at the water's edge.

**Build (41 places in 7 categories).** Pick a category chip (Homes, Town, Farm, Fun, Travel, Nature, Small), then an
item in the row below:
- **Homes:** cottage, townhouse, thatched, apartments, chalet
- **Town:** church, town hall, bakery, café, grocer, school, fire station, fountain
- **Farm:** barn, windmill with turning sails, silo, greenhouse, fields of wheat, sunflowers or lavender, vegetable garden
- **Fun:** carousel, Ferris wheel, playground, bandstand, castle ruin, lookout tower, campsite
- **Travel:** station, water tower, signal box, fuel station
- **Nature:** boulders, crag, log pile
- **Small:** lamp, bench, flower bed, well, statue, signpost, picnic table, hay bales

Houses get a flat pad cut into the slope. The **Station** snaps beside the railway with a platform. Homes bring their
residents with them.

**Paths.** Roads, country lanes, footpaths and hiking trails, plus fences, hedges and stone walls, all drawn with one
stroke:
- Start or end a stroke on another line and they join.
- Roads and lanes smooth the ground across their width.
- A fence drawn into a closed loop makes a pasture: animals placed inside stay inside.
- Where a way crosses the railway, a level crossing appears, with planks and crossbucks. It is the only place villagers
  cross the rails.

**Bridges.** Wherever a railway, road or path crosses a pond or stream, a bridge appears by itself, in a style that
suits it: stone arches for railways and roads, a timber trestle for lanes, a humped footbridge for paths, stepping stones
where a trail meets a narrow stream.
- **Tap a bridge** with the Track or Paths tool to cycle through the other styles that fit it: steel truss,
  suspension (long spans only), covered bridge, and so on. Your choice is remembered, saved and undoable.
- **Shared decks:** draw a second track, or a road, beside an existing crossing and the bridge widens to carry both.
- **Walking across:** villagers walk over road and path bridges.

**Life.** Villagers, sheep, cows, horses, pigs, hens, ducks and deer:
- **Villagers** use what is built around them. They sit at the café's tables and on benches, gather round the
  fountain and on the church steps, browse the grocer's crates, play in the playground, ride the carousel and the Ferris
  wheel, climb the lookout tower, and call at their neighbours' doors. They walk along roads and paths, hike the trails,
  and take the train.
- **Animals** each keep to their habitat. Grazers stay on grass and in their pasture, hens peck about the yard, ducks
  paddle on ponds, and deer keep to the woods and bolt when someone comes near.

**Mountains.** The **Peaks** terrain mode raises craggy summits. High ground turns to scree and rock bands, with snow on
the tops and boulders on the slopes. Ponds get sandy beaches.

Palette: **Terrain · Water · Forest · Paths · Track · Train · Build · Life · Eraser** tools, an options row for the current tool, and
**Undo · Save · Load · Move · New**. The world saves automatically when the app is paused or closed and is restored
on the next launch.

## How it is built

Everything you see is generated at runtime by C# from seeded procedural generators sharing one palette and a few
shaders: no downloaded or purchased models, textures or services.

| Area | Code |
|---|---|
| Mesh toolkit, noise, palette | `Assets/Wonderland/Scripts/Core/` |
| Trees (5 kinds, 17 variants), meadow plants, houses (cottage, townhouse, thatched, station × 4 styles), sheep, cows, villagers (6 outfits), locomotives, coaches and railcars in 6 liveries, 4 goods wagons | `Assets/Wonderland/Scripts/Assets/` |
| Terrain field + renderer, water, streams, forest, ground cover, railway network (graph, planner, mesh with switches, signals, bridges, tunnels), trains (reservations, routing), buildings + platforms, animals, villagers, world (undo / save) | `Assets/Wonderland/Scripts/World/` |
| Palette UI, controller input, tools | `Assets/Wonderland/Scripts/Interaction/` |
| Boot: permission, MRUK floor + free space, placement, autosave, foveation; frame-timing log (`PerfLog`) | `Assets/Wonderland/Scripts/App/` |
| Shaders: `Wonderland/Lit` (vertex-coloured storybook shading, wind sway), `Wonderland/Terrain` (procedural grass, flowers, soil, rim fade, brush ring), `Wonderland/Water` (depth tint, ripples, glints, shore foam, flow), UI, steam | `Assets/Wonderland/Shaders/` |
| Scene + Quest configuration, build, tests, renders | `Assets/Wonderland/Editor/` |

The project settings were seeded from the EasyPiano Quest project (same Unity / Meta XR / URP stack); the
application is `com.splink.wonderland`.
