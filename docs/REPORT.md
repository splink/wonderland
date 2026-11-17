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
| Villagers | ✓ Decor tool, Villagers: 3 cm figures in 6 outfits (straw hats, flat caps, bonnets) with swinging arms and legs. They visit neighbours' doorsteps and gesture as they chat, take train rides, and turn and wave whenever a train passes within about 24 cm. They keep to dry land, off the rails (except at level crossings) and out of houses; they move house if a pond or building covers theirs. Their days are described under [A living village](#a-living-village) | Journey (4 simulated minutes): stroll, call at a door, wait on the platform, board, wave, never on rails / in water / in a house, eraser, saved | ✗ |
| Railway network | ✓ Extend, branch, join, cross (diamond), loop, bend by grip, erase one piece; up to 8 trains (steam, goods with 4 wagons, railcar) in 6 liveries; automatic reservations, switch and stop signals, Explore / Random / Manual switches, per-train speed, lift and re-place trains | Journey: figure 8 crossing; branch to a dead end; extend; join; Manual and Explore switches; three trains for 3 minutes never closer than 5.5 cm between cars and none jammed; two crossings shared; head-on resolution; red / green signal; pinned switch; bend; erase + undo; save/load. Interaction: drawing a branch and tapping its switch with the controller, placing a running train, Stop all / Start all | ✗ |
| Object library | ✓ 41 places in 7 categories (homes, town, farm, fun, travel, nature, small details), chosen in two rows of chips (category, then item). Turning parts: the carousel, the windmill's sails, and the Ferris wheel with upright cabins. Small details may stand close to buildings; rocks and camps follow the ground instead of getting a pad | Journey: every object builds (largest 16.5 k vertices); a village square goes up. Interaction: category and item chips place a fountain | ✗ |
| Paths, fences, pastures | ✓ One drawing gesture for roads, lanes, footpaths, trails, fences, hedges and walls. Ends snap onto lines of the same family; roads and lanes grade the ground; closed barriers make enclosures; ways crossing the railway get level crossings. A walking graph with A* gives villagers routes | Journey: a road smooths the ground across it, a path joins it, a route is found, no houses on the road but lamps beside it; a fenced pasture keeps its sheep in and the horse outside out; a level crossing lets villagers cross the rails there and only there; undo and save/load | ✗ |
| Living world | ✓ Places bring activity spots (door, seat, gathering point, play, ride, view, browse). Villagers choose among them by need and hour (see [A living village](#a-living-village)); claims stop two people taking one chair. Riders sit on the carousel's horses as it turns, or vanish into a Ferris wheel cabin. Homes bring residents. Hikers walk the trails. Animals keep to habitats: pasture grazers, hens, ducks on ponds, deer by the woods that flee | Journey: residents move in; villagers sit, gather and browse; visitors ride and play at the fair; a hiker sets off; a duck stays on the pond; deer refuse open ground, settle by the wood and bolt from a villager | ✗ |
| Mountains and beaches | ✓ Peaks brush (ridged noise); the terrain shader adds alpine scree, layered rock bands on cliffs, snow on the tops and sand along pond shores; ground cover adds boulders and alpine flowers | Journey: the Peaks brush raises a craggy mountain | ✗ |
| Bridges | ✓ Derived from what crosses water (railway pieces and ways): crossings close together and parallel share one deck; 7 styles (stone arches, timber trestle, steel truss, suspension, covered, footbridge, stepping stones) offered by what the bridge carries and its span; a tap in the Track or Paths tool cycles them; choices are remembered by place (saved, undoable); ways rise onto the deck and villagers walk across | Journey: rail bridge over a pond; a parallel second track shares and widens it; tap restyles; survives rebuild, save/load, undo; road bridge deck 26 mm above the water and walkable; stepping stones for a trail, a footbridge for a footpath; suspension only for long spans | ✗ |
| Eraser, undo, save/load | ✓ eraser taps remove a house, a stream, an animal, a villager, the train or the railway, and holding it clears trees; undo restores whole-world snapshots (depth adapts to a 48 MB budget); Save/Load to one local JSON file, plus autosave on pause/quit and restore at launch | Journey: undo; save/load round trip preserves terrain exactly, trees, houses, railway and the running train (190 KB). Interaction: B undo, palette Undo, eraser tap | ✗ |
| Palette UI | ✓ panel on the left controller: eight tool tiles with live 3D models of what they make, a contextual options row, a hint line, a status line, and Undo · Save · Load · Move · New (New needs a second press to confirm) | Interaction test clicks tiles, chips and buttons by ray; layout checked in `editor-palette.png` | ✗ |

| A day in the village | ✓ Village clock, needs and routines, homes, shopkeepers, walking regions and routes, train journeys between regions, lit windows, night lighting: see below | Journey: the village-life section (30 checks) | ✗ |

**Automated tests** (Unity batch mode on this Linux machine):
- `scripts/unity.sh test` runs **287/287 checks PASS** (`scripts/unity.sh village` and `scripts/unity.sh town` run just the village or the town sections, in about a minute each). It is the scripted journey against the world API that the controllers drive.
- `scripts/unity.sh interaction` runs **36/36 PASS**. It drives the real `ToolController` and `PaletteUI` with synthetic controller rays and buttons (UI hit-testing, tool switching, strokes, track drawing, placement, undo, eraser, the clock chips, the Fête chip, the Clouds chip, the Watch tool).

What the tests do **not** cover: OVRInput and tracking, passthrough compositing, MRUK on a real room, the permission
prompt, comfort, legibility at real viewing distances, and frame rate.

## A living village

The village now lives a day at a time, and what the player builds decides how. Building blocks, composed:

| Block | What it is | Where |
|---|---|---|
| **Clock** | Time of day: a day takes about 7.6 minutes, the small hours run three times faster. Normal / fast / stopped; saved | `VillageClock.cs` |
| **Needs and the daily rhythm** | Six needs (shopping, food, company, fun, sights, rest) plus work for shopkeepers. Each grows at its own rate. One table says how much each appeals at each hour, and that table is the whole "schedule". Each villager has fixed leanings (0.6–1.4 per need) from their seed, and a name | `Needs.cs` |
| **What places serve** | Each of the 41 catalogue entries declares the one need it serves; its existing activity spots are its capacity. Homes serve rest; shops with residents are their keepers' homes and workplaces | `Catalog.cs` (`Serves`) |
| **Where people can walk** | A 2.5 cm grid over the land: walkable cells, links not crossing fences, streams or rails (except at level crossings), and which cells are on a road or path. It gives connected regions (reachability) and A* routes that prefer roads and go round obstacles. It rebuilds in the background after edits, at most 2 ms per frame | `WalkGrid.cs` |
| **Intent** | A decision: what someone means to do (a place's spot, home, bed, a neighbour, a hike, a stroll, a train ride, a wander), for which need, and how (on foot, or by train between two stations) | `Needs.cs` (`Intent`) |
| **Choosing** | A directory of places, rebuilt when buildings, stations or regions change, with free spots, regions, novelty and beds. Each option is scored as need × appeal at this hour × leaning × nearness × novelty. Options that can't be reached on foot or by train are dropped. One is picked with weights favouring the best, so a villager won't always take the top choice. Bedtime overrides everything | `PeopleSystem.Life.cs` |
| **Carrying out** | The existing walking, steering, activity and pose code, now driven by intents: walk the route, use the spot (sit, browse, go inside, work, ride), then satisfy the need. At most one decision per frame | `PeopleSystem.cs` |

**What the player sees:**
- Mornings at the bakery and the grocer, lunch at the café, afternoons at the fair, evenings at the fountain. Shopkeepers
  stand in their doorways during opening hours.
- At bedtime people wrap up and walk home. Windows glow where someone is in, go dark where nobody is, and go out after
  midnight. The rides stop and animals settle down.
- The key light follows the clock: an east-to-west sun, a golden evening, a moonlit night. Lamps glow brighter at night
  and ponds dim.
- **Consequences of building:**
  - A new place draws the curious (a novelty bonus for half a day), and the palette announces its first visitor.
  - Removing a place sends people elsewhere.
  - New homes bring residents, who use the shops around them.
- **Reachability:** a railway, a fence or a pond splits the village into regions, and nobody sets off for what they
  can't reach. A level crossing, a bridge, or two stations with a passenger train reconnect it: villagers walk to the
  platform, ride, get off at the far station and walk on, and come home the same way.
- **Explanations:** pointing at a villager shows their name, what they're doing and why ("Kit - sitting at the café
  (lunch)"). Pointing at a building shows its visitors and when it's busiest.

**Design decisions:**
- **Data over special cases.** No building has its own behaviour code. A place declares a need and has spots; the
  rhythm is a single table; reachability is one grid.
- **Deciding is separate from doing.** `Choose` returns an `Intent` and `Carry` executes it. Another decider (a
  schedule, a story, eventually an external agent) could hand villagers intents without touching movement.
- **Derived, not stored.** Homes are matched to residents from doorsteps, and places and regions are recomputed from
  the world. Only the clock and each villager's needs are added to the save (format v4; v1–v3 still load).
- **Bounded cost.**
  - Decisions are event-driven (when an activity ends), and there is at most one per frame.
  - Sleeping and indoor villagers cost almost nothing.
  - The grid rebuild is time-sliced; the places directory is rebuilt only when something changes.
  - `InsideHouse` gained a distance pre-check.
- **Night glow** uses a few shared material variants rather than property blocks, so it keeps SRP batching. The editor
  renders showed property blocks being ignored under the desktop renderer's GPU Resident Drawer.
- **Deferred from the first slice** and since built (see [The working village](#the-working-village)): opening hours,
  jobs beyond shopkeeping, produce, trains chosen by destination, friendships, and plans from outside.

**Also fixed:** level crossings were narrower than the rail bed, so villagers could never actually walk over them.
Before the walk grid nobody tried; the crossing now spans the bed.

**Verified (editor, batch mode):** the village-life section of the journey checks:
- the clock;
- the daily rhythm (errands 24 % of visits in the morning vs 3 % in the afternoon, fun 17 % vs 3 %, evening company
  65 % vs 3 %);
- everyone asleep at home at 02:00, and the streets empty;
- rides stopped at night; windows lit and dark;
- shopkeepers working only in opening hours;
- no spot shared and no place over capacity;
- nobody inside a railway loop heading for an unreachable place;
- a new café visited (first after about 12 s) and announced; its removal re-routing its visitors;
- removing a building at night leaving sleepers asleep;
- riding the train across the loop and walking on; walking over a new level crossing;
- save/load of the clock, needs and homes;
- a world with no places;
- 40 villagers.

**Unverified:**
- Everything on the headset.
- Readability of the night lighting through passthrough.
- The editor timings below, measured on a desktop CPU under Mono, which don't predict Quest 3 under IL2CPP.

### The working village

The second slice adds work, produce, friendships and plans from outside. It uses the same building blocks and adds
three more, each data plus a little code:

| Block | What it is | Where |
|---|---|---|
| **Opening hours and jobs** | Per place in the catalogue: opening hours (shops, the café, the fair, the school), one job ("baker", "farmer", "teacher", "station master"; 15 kinds of place), the shift, indoors or out, and the place's goods role (grows, sells or ships) | `Catalog.cs` |
| **Jobs** | Each job goes to the nearest willing villager who can get there, on foot or between two stations, closest pairs first. Whoever lives over a shop keeps it; a quarter of villagers don't work (fixed by seed). Re-derived whenever the world changes; new hires are announced | `PeopleSystem.Work.cs` |
| **Produce** | A few crates per place. Farms grow them while their farmer works; shops and the café sell a quarter crate per visit; bare shelves cut a shop's draw by more than half; a van tops shops up slowly. Workers carry crates by hand within their walking region (`Haul` intent: collect, then deliver). Goods trains standing at a station load what waits on the platform and unload where that side of the line wants crates | `PeopleSystem.Work.cs`, `Train.Cargo` |
| **Trains by destination** | `TrainSystem.StopsAhead` follows the line from a train's centre as the switches are set now, turning back at buffers and dropping platforms it can't centre on before them. Passengers board only a train that calls at their station within three stops; if a switch sends theirs elsewhere they get off and change. Cached per train until it moves or a switch changes | `TrainSystem.cs`, `PeopleSystem.cs` |
| **Friendships** | Once a second, villagers at the same place accumulate acquaintance (more when sitting or gathering together); after 45 s they become friends (at most five each). A friend's presence makes a place 1.8× as attractive; calling on a friend brings them out to the door. Saved as friends' seeds | `PeopleSystem.Friends.cs` |
| **Plans from outside** | `IVillageDecider`: asked before a villager chooses for themselves (not at bedtime). `PlanVisit`, `PlanGather` and `PlanHome` build plans the same way the villagers do; every outside plan is checked (place still there, spot free, way walkable or by train) before it is carried out, so a naive decider can't break anything. `Direct` interrupts someone now. The fête is the first decider: the Life tool's **Fête** chip invites everyone free to the main gathering place for 2.5 hours | `PeopleSystem.Plans.cs` |

The save format is now v5: it adds the crates kept at each place, each train's cargo and each villager's friends. v1–v4
saves still load.

**What the player sees:** the baker in the bakery doorway from six, the farmer hoeing at the edge of the wheat field,
the teacher's classroom lit, a gardener walking a crate of vegetables to the grocer, a goods train taking crates
across the line. Shops shut and the carousel stops in the evening. Pointing at a shop shows its keeper, opening hours
and how full its shelves are; pointing at a villager shows what they carry and who they are with. The palette announces
new hires, first deliveries, bare shelves and new friendships. `editor-day-work.png` and `editor-day-fete.png` show the
farmer at work and a fête on the square.

**Also fixed along the way:**
- A villager visiting the church across the village late in the evening walked home until half past three. People now
  only go out in the evening if they can be back by bedtime, and anyone walking home at bedtime goes straight to bed.
- A walker that stops making progress re-routes once, then gives up and chooses again.

**Verified (editor, batch mode), the working-village section of the journey:**
- opening hours from the catalogue; nobody shopping at the bakery after it shuts;
- jobs assigned (6 of 6 places staffed in the test village, the baker over the bakery); shifts kept (the teacher
  indoors, the farmer out in the field, nobody working at night);
- farms growing, crates carried, the first delivery announced; bare shelves announced and shown;
- friendships formed (4 in two days) and kept through save and load, with the shelves' stock;
- a fête gathering 7 of 10 villagers and dispersing when it ends;
- an outside decider filling the café to its spots, and a nonsense decider ignored;
- a goods train carrying crates from a farm inside a railway loop to a bakery outside it;
- on a loop with a branch line: the destination board following the switch, a passenger letting the wrong train go,
  then riding to the branch station and walking on.

**Unverified:** as for the first slice: nothing of this has run on the headset.

### The livelier town

The third slice adds a town that works: new places, traffic, emergencies, pets, sound, CCTV, clouds and balloons.
Each is joined into the existing blocks (catalogue places with spots, hours and jobs; needs; intents; the walk
grid; the clock; save) rather than run beside them.

| Block | What it is | Where |
|---|---|---|
| **New places** | Park, museum, restaurant, disco, hospital, police station, factory, football pitch, monitoring studio, CCTV camera, hot-air balloon: 11 catalogue entries with meshes, activity spots, opening hours, jobs and goods, so villagers use them through the existing choosing. A place can carry its own rhythm (the restaurant at supper, the disco after dark), and a **Services** category groups the civic buildings | `Catalog.cs`, `HouseFactory.Town.cs` |
| **Children and night owls** | Fixed by a villager's seed: a fifth are children (smaller, early to bed, at school in the morning, twice as keen on the playground and the pitch, never at the disco). Whoever lives over a shop is a grown-up. A quarter of grown-ups are night owls, whose evening runs later. Everyone now sets off home in time to be there by bedtime, counting the faster night clock | `PeopleSystem.cs`, `PeopleSystem.Life.cs` |
| **Football** | Players on a pitch make a match: one ball per pitch, passed between them, a quarter of kicks a shot at the far goal; players run after it around their positions; a lone player shoots at goal | `PeopleSystem.Games.cs` |
| **Traffic** | A graph derived from the drawn roads and lanes (a node every 3 cm; junctions where ways meet; footpaths for bicycles only). Households beside a road get a car, a motorbike or a bicycle, fixed by where the house stands (at most 14). By day they make trips to open places 25 cm to 2 m away, park at the kerb, and come home. Pure-pursuit steering on a route offset to the right; they slow behind traffic, stop for people on the road and at level crossings while a train is near, and edge on after four seconds so nothing deadlocks. Derived, not saved: they come back with their homes | `TrafficSystem.cs`, `VehicleFactory.cs` |
| **Emergencies** | One incident at a time, every 2.5–5 village hours by day (or on the Life tool's Emergency chip): someone out of doors feels faint (ambulance), smoke from a roof (fire engine, with a hose spray), a missing bicycle (patrol car). Lights and siren there, a short scene, back to the station. It ends quietly if there is no road there, the vehicle or place goes, or it takes over two minutes | `TrafficSystem.Incidents.cs` |
| **Pets** | Two more animal kinds. A dog belongs to the nearest household: garden by day, doorstep at night, and when its owner is out it follows their own footsteps (so never through a house or over a fence), waits outside where they went in, and goes home the way it came. A cat keeps to a 12 cm territory, naps, perches on walls and fences, and dodges dogs | `AnimalSystem.cs`, `AnimalFactory.cs` |
| **Sound** | 12 loops and 9 moments synthesised at start-up (no recordings, about 30 s of 22 kHz mono). Every quarter second the audible things are scored by loudness and nearness, and the best 8 play through a fixed pool of 3D sources that fade rather than restart. Moments go through 3 more sources, rate-limited per kind and only within earshot (2.4 m). On / Quiet / Off is kept in `PlayerPrefs` | `SoundSystem.cs`, `Core/SoundBank.cs` |
| **CCTV** | Cameras are catalogue objects with an aimable head; aims are saved (format v6) by where a camera stands. One hidden camera films on demand into 320 × 240 pictures: the watched feed about 15 times a second while the Watch tool is open, the studio's four screens in turn (a screen every 0.3 s, only while a studio is in view), and at most one picture per frame. Only the miniature world (never the passthrough room), no shadows, no post-processing, no MSAA | `CctvSystem.cs`, `Wonderland/Screen` shader |
| **Sky** | Eight cloud meshes drifting a metre up and casting shadows; balloons fly a 100-second loop from their launch field by day and land for the night (riders ride the basket like a Ferris wheel cabin) | `SkySystem.cs`, `BuildingSystem.cs` |

**Design decisions and simplifications:**
- **Traffic is coherent, not a simulator.** Nobody drives a particular villager's car: households own vehicles, and
  their trips follow the same daily rhythm as the villagers (busy mornings and lunchtimes, parked at night). Linking a
  villager's long trip to their car is the obvious next step.
- **Incidents are three small scripted scenes.** They are dispatched by the traffic system on its own vehicles, not
  by a general mission engine.
- **CCTV films on demand.** A wall of continuously rendered feeds would be the most expensive feature on Quest, so
  the viewer is the only fast feed. Studio screens refresh in turn and only while visible; the cost is bounded at one
  small, shadowless render per frame.
- **Sound is synthesised.** That avoids licences and asset weight, and suits the storybook look. Loudness is scored,
  not every source played: eight loops at most.
- **Derived state again.** Vehicles, owners, children, schools and matches are worked out from the world. Only camera
  aims are new in the save (v6; v1–v5 load), and pets are ordinary saved animals.

**Fixed along the way:** a person feeling faint could be "rescued" by the walkability check or start waving at a
train; and an ambulance creeping towards a patient on the road stopped for her every few seconds. Responding
vehicles now pull up short of the scene, and any vehicle that has waited four seconds edges on for a moment and a
half.

**Verified (editor, batch mode), the town section of the journey:** 
- every new place, vehicle and pet has a mesh; every sound is synthesised, audible and unclipped (21 clips);
- the roads form one network, joined where they cross;
- homes have vehicles, and the stations have theirs;
- vehicles stay on the roads all day (worst 2.2 cm from a centre line, against a 2.6 cm road half-width) and make
  round trips;
- the restaurant is busier at supper than in the afternoon, and the disco busy after dark and empty by day;
- the museum and park get visitors; children go to school and play football with a ball (passes and goals);
- the factory smokes while worked;
- the dog follows its owner and never strays; the cat stays in its territory and perches on the wall;
- the balloon flies by day only, and clouds drift;
- the ambulance, fire engine and police car each answer a call with lights and siren, deal with it and park back at
  their station;
- the watched CCTV feed is filmed 15 times a second, and the studio wall about 3 pictures a second between its four
  screens;
- camera aims, pets and vehicles survive save and load, and erasing a camera keeps the selection and aim of the other;
- the sound pool plays at most 8 sources and is silent when Off; repeated barks are rate-limited; out-of-earshot
  moments are skipped;
- taking a road away re-routes or parks the traffic;
- vehicles wait at a level crossing while a train passes and are never on it with the train;
- a busy town (40 villagers, 8 vehicles, pets, sound and a feed) costs 0.4 ms of simulation per frame on average, worst
  6 ms (editor, simulation only - not rendering).

`scripts/unity.sh town` runs this section alone (about a minute). The controller test also drives the Watch tool
(the live viewer appears, tapping the land aims the camera, the viewer goes away with the tool) and the Clouds chip.

**Unverified:** everything on the headset.
- What a CCTV render costs on Quest 3: one 320 × 240 shadowless picture per frame at most, but untested.
- How the synthesised sounds come across through the Quest speakers, and whether the levels suit a room.
- Readability of 2 cm vehicles and pets at arm's length.
- Clouds at a metre: they may get in the way of someone kneeling close to the model; the Clouds chip turns them
  off.

### Coherent lives, a reliable railway, and what the headset is doing

This round started from a measurement, not a guess. On the Quest the player's world (218 villagers, 124 buildings,
108 animals, 5 trains, 25.8 m² of land) ran at **35–37 fps** with the CPU main thread at **22–26 ms per frame**. The
GPU time read 0 (not reported by Unity's frame timing for this setup). 206 of the 218 villagers were walking at 14:00
and 2 were at a place. The save was pulled from the headset (`Tests/Saves/quest-world.json`, kept out of git) and is
now a benchmark: `scripts/unity.sh questworld`.

**Performance, root causes and fixes** (editor CPU, the player's world, 72 steps a second):

| | Before | After |
|---|---|---|
| Whole simulation per frame | 44.7 ms | 6.0 ms |
| Villagers | 39.5 ms | 3.8 ms |
| Animals | 4.7 ms | 2.1 ms |
| Clouds | 0.4 ms | 0.01 ms |
| Walk-grid rebuild after an edit (work) | 4.2 s | 0.9 s |
| Allocation per frame | 0 | 0 |

- **Walkability checks were the bottleneck.** About 830 checks per frame took around 46 µs each, scanning all the
  buildings, the whole railway and every stream point:
  - The walk grid already knew which ground is open, so a cell that is open and linked on all four sides now answers
    at once.
  - The exact check searches only the track cells within 3 cm instead of 30 cm.
  - Streams are skipped by bounding box.
- **Smaller fixes:** the clouds cache the land's bounds, and the sounds are synthesised one a frame after start-up
  instead of on first use (a 14–29 ms hitch).
- **Every system's cost is measured** (`SimCost`, two timestamps per system per frame) for the Stats panel.

**Inhabitants: purposeful days.** Root causes:
- Every villager's fallback was "wander somewhere within 30 cm of home", so with places full or far away, wandering
  dominated.
- Over 130 of the 218 villagers had been set down by hand far from any free bed (homes had one spare bed, and moving in
  looked only 25 cm away). Their home was a meadow, which explains the people in remote countryside.
- Hikes and strolls weren't limited.

Fixes:
- **A day plan per villager:** shopping at their usual shop, work with a lunch break (or school), an afternoon out, an
  evening out, home, with real places that are open and reachable (by train if far).
- **Weighting:** the plan is the strongest option at its hour, but needs still decide, and favourites persist.
- **No aimless wandering:** villagers with a home never wander. The homeless keep to their own patch. Homes take a
  family. Moving in searches a metre around.
- **Limits:** hikers and strollers are capped (and planned, for people living by a trail). A place someone failed to
  reach is avoided for three hours.
- **Bedtime:** journeys under way at bedtime (or to a place that has closed) turn for home, and visits wrap up when
  their part of the day ends.
- **On the player's world** at 16:00, the walkers are now heading somewhere specific: to places, to work, home, or
  (a capped few) on the trails. Its 136 homeless villagers remain; there are no beds for them.

**The character card:**
- A trigger on a villager or animal in the Life tool opens it. A villager's card shows a small turning model, name,
  job and life stage, lasting traits (night owl, early riser, what they love, from the same seed-based leanings that
  drive their choices), home, workplace or school, what they are doing now, where they are going and why, the next part
  of their plan, and their friends.
- A marker follows them, a line shows the way they are walking, and pins mark their home and work.
- Animals show their name (pets and farm animals; deer and ducks are wild), whose they are, what they are doing and
  where they are going.
- Everything on the card is read from the simulation. The test compares the card's "going to" with the place each
  walker is actually heading for (400 of 400 matched).
- Gender is not modelled, so the card doesn't show it.

**Railway: what was a defect and what was missing.** The new scenario tests draw track the way a player does:
- **Branches** at 20–90 degrees from every side of a loop: they worked (44 of 44).
- **Loops drawn in two halves:** they worked.
- **Linking two loops was a defect.** The stroke was bent between two fixed switch leads and failed "curve too tight".
- **Short extensions from a dead end were a defect.** The stroke was trimmed away near the attachment, so they failed
  "draw a longer track".
- **The fix:** when shaping fails for an attached stroke, the planner now fits a smooth curve that leaves and meets
  each attachment along the track and follows the stroke (cubic Béziers, directly or through the stroke's middle; the
  gentlest wins).
- **Closing a loop "sometimes":** a stroke that starts or ends within 7 cm of existing track is a connection, not a
  loop. That is by design; the preview's snap markers show it, and the message after the stroke now says exactly what
  was built (loop, branch with a switch, joined at both ends, extended).
- **Signals were static by design** (manual stop signals). They are now automatic (red while any train holds track
  within 40 cm), and tapping holds them at red. Old saves keep their red signals held; green ones become automatic.
- **Wagons were not implemented.** Carriages are now 0–6 (steam), 0–8 (goods) or 1–3 (railcar), saved per train, and
  the engine stays in place when they change.
- **Diagnosis:** pointing at a train says what it is doing and why it waits, and its next stop. Stations are named.

**Help, statistics and worlds:**
- **Help:** a panel with the controls of the tool in hand (written from the code: every gesture listed is
  implemented) and the general buttons, including B for Undo, which used to be undocumented.
- **Redo** is new.
- **Stats:** a panel refreshed twice a second only while open. It shows frame rate, frame time against the headset's
  budget, CPU main-thread time, GPU time or the headset's own GPU utilisation when reported (otherwise "not
  reported"), simulation cost by system, and the world's counts.
- **Worlds:** Save and Load now keep World 1–6. The world in play is shown in the palette title and saved into its
  slot when the app is left.

**Validation (editor, batch mode):**
- `scripts/unity.sh test`: 287/287. It adds a daily-life journey (everyone out during the day, most at two or more
  places; work and school; plans followed; no aimless wandering; the card truthful; nobody stuck; everyone home in
  bed; a removed place drops out of the plan) and the railway journeys (two-stroke loop, 44-branch matrix, two loops
  linked and a train through both, T-join, extension, carriages, automatic and held signals, two trains without
  touching, a network saved and loaded).
- `scripts/unity.sh interaction`: 36/36. It adds Watch, Clouds, Worlds 1–3 saved and loaded, Help and Stats, the card
  by trigger, and Redo.

**On the Quest** (the player's own world, the player using it; `Wonderland perf` lines and Perfetto traces):

| Build | Frame rate | CPU main thread | GPU (Perfetto) |
|---|---|---|---|
| Before this round | 35–37 fps | 22–26 ms | not traced |
| Simulation fixes | 13–25 fps (one 5 s window at 60) | 9–12 ms | 47 ms average, 103 ms worst: GPU-bound |
| + small figures culled beyond 3 m, limb shadows off | **37–47 fps** | 10–11 ms | — |

- **The CPU work is fixed.** What remains is GPU work: the trace shows the app's GPU time far over the 13.9 ms budget,
  with the main thread busy about 12% of the time.
- **Where it goes:** a 25.8 m² world with about 125 buildings at 7–14 k vertices each, about 360 trees, and more
  than 200 villagers and 100 animals, drawn for both eyes plus a shadow pass.
- **Culling helped**, but it doesn't reach 72 fps on this world.
- **Next steps for the GPU:**
  - simplified meshes (LODs) for distant buildings and trees;
  - adaptive render resolution;
  - a lighter terrain shader far from the player.
- Unity's frame timing reports no GPU time on this setup, so the Stats panel shows the headset's GPU utilisation
  when it is reported, and says when it isn't.

**Limitations:**
- Villagers without a free bed (136 in the player's world) still have no home. Build more homes and they move in.
- Animals cost 2 ms on that world: grazers check every building when they move. A spatial index would be the next
  step.
- The walk-grid rebuild after an edit is spread over several seconds on a large world.
- Ahead-of-time train routes follow the switches as set; in Random mode passengers sometimes change trains.

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

**Not yet measured:** no `Wonderland perf` lines or Perfetto traces exist. The headset was idle during an earlier
session, so the app stopped on launch. On 2026-11-16 the working-village build was installed and launched, with the
proximity sensor briefly overridden and then restored. The headset was lying in the dark, though: it had lost tracking,
and the system's "searching for position" dialog covered the app, so no frames were logged.

**Villager simulation, editor timings only:**
- 40 villagers in a 20-place village:
  - about 1.4 ms per frame on average, worst frame about 7 ms;
  - about 1.4 decisions per second;
  - slowest single decision (choice plus route) about 3.5–5.5 ms;
  - places directory rebuild about 1 ms.
- Walk-grid rebuild after an edit: about 120 ms of work spread over about 50 frames (worst frame about 5–10 ms).
- The `Wonderland perf` log line now carries a census ("40 people (9 asleep, 6 walking, 12 at places, 4 at work, 3
  friendships), 14:20").
- With jobs, produce and friendships added: 40 villagers at about 1.2 ms per frame on average, worst frame about 8 ms.

These are editor numbers on a desktop CPU and must be measured on the headset.

Static budget (`docs/renders/asset-budget.txt`):
- trees: 0.4–2.5 k vertices
- houses: 7–14 k vertices (station 7.5 k)
- villager: 642 vertices (5 parts)
- sheep: 828 vertices; cow: 574 vertices

Each animal or villager is 2–5 renderers, so 30 villagers add about 150 draw calls (SRP batcher, one material). If that
shows up in a trace, the next step is GPU instancing, or merging the body into one skinned mesh. Render scale 1.0,
MSAA 4× and one 1024 px shadow map remain the first knobs on the GPU side.

## Discoverability, weather and the world's headroom

From play-testing on the headset:

| Asked for | What changed |
|---|---|
| A camera on a train | Every train carries one on the front of its engine; the Watch tool lists them after the pole cameras (Train 1, 2, ...). |
| How to add wagons | **+ Carriage / - Carriage** chips in the Train tool act on the train last pointed at. |
| Stream between two lakes looks unnatural | The stream used to take the pond *bed* as its water level and carve a channel through the pond (a ditch and a step). Now: in a pond the level is the pond's surface, the channel isn't carved inside ponds, it enters at full width, and it is reversed if drawn uphill. |
| How to find out who someone is | Pointing at anyone with any tool names them on the palette; grip opens their card. |
| Track direction matters | A switch only sends a train into its branch when the train comes from the stem. While drawing, a blue arrow on the old track shows the direction that can take the new line. In Explore mode trains turn back at stations now and then (only when no other train is near), so they use branches facing either way. |
| Rotation when placing (cameras) | A flick turns 15°, holding turns smoothly; an arrow shows the front, and a camera shows the edges of its view. |
| More clouds, movement, rain | 14 clouds on a veering wind; showers about one in ten village hours (Rain chip on demand): grey clouds, rain particles over the land, dimmer light, a rain sound, villagers avoid open-air places. |
| A clock and a green/orange/red light | Always on the palette: the measured frame time against the display's budget (green ≤ 1.08×, orange ≤ 1.5×, red beyond). |

Validation: 298/298 journey checks (new: weather, streams between ponds, train cameras), 39/39 controller checks
(new: carriage chips, grip-to-card in the Build tool, 15° turns, the clock). Renders: `editor-weather-shower.png`,
`editor-weather-stream.png`, `editor-train-camera.png`.

Limitation: when a train runs tender-first after turning back, its camera looks backwards along the line.

## Boats, building cards, a world that stays put, and a railway bug

From the next round of play-testing:

| Reported | Cause / what changed |
|---|---|
| Trains and wagons off the track for a few seconds | **A real bug.** A line drawn from a dead end back onto its own piece of track: splitting that piece for the finish moved the start's track end onto the new half, so the switch listed an end 3.2 m away (or a piece got a jump inside it) and trains taking it cut across. Fixed where the line is attached; saved railways are repaired when they load (`TrackNetwork.Heal`). Found with a probe that checks every car against the rails on the player's own world (it reproduced 20 cm excursions; healed: none in 400 s). |
| World under or above the floor, drifting | Its position wasn't saved and wasn't tied to the room: placed in front of the head at every launch, at the floor height read at that moment. Now pinned to a spatial anchor (saved on the headset) and followed every frame; height from the room scan's floor. Not testable in the editor. |
| A train going backwards | **Turn round** (Train tool); trains run round at buffers and after backing away. |
| Turn a camera | Stick in the Watch tool turns and tilts the watched camera. |
| Better cards | Fixed-size sections and two-column rows (`CardText`); taller panels; person, building, stats and help. Renders: `editor-cards-person-stats.png`, `editor-cards-building-help.png`. |
| Umbrellas | Villagers outdoors (or queueing for a boat) open one in a shower; five colours, each at its own moment. `editor-umbrellas.png`. |
| Building cards | Hours, open now, visitors now (named) and per day, staff, goods, busiest time; residents and beds; trains calling at a station. Grip a building (Life tool: trigger). |
| Boats and landing stages | Rowing boat, sailing boat, paddle steamer; landing stage at a pond's edge; boat trips for villagers. `editor-pond-jetty.png`, `editor-pond-boats.png`. |
| No sound | Not reproduced: on the player's world the editor plays birds, water and the fair from where the player stands, and every sound synthesises. The headset logged nothing chosen to play, which suggests an error there; the sound loop now survives and reports errors, and its state (listener, ears, volume, errors) is in the headset's perf log line for the next session. |

## The railway, reworked: one way, previewed, spread out

Reported: joining two loops "more often than not" failed although the drawing looked fine; direction unclear; trains
running in opposite directions; trains bunching in one part.

| Finding | Change |
|---|---|
| The preview while drawing showed only the raw stroke; the planner ran on release, so its refusals came as a surprise. | `WonderlandWorld.PreviewTrack` runs the same planning (on a copy of the network) ~5 times a second while drawing: green line + arrows, or red + the reason. Tested: on 60 hand-drawn links preview and build agree 60/60. |
| Many links failed "curve too tight" where they left a loop at a sharp angle. | The planner slides the new switch up to 18 cm along the track to make room (`TrackPlanner.Slide`). Joining two loops: 51/60 -> 56/60 built; the remaining refusals are shallow crossings and track too close to other track. |
| Trains in opposite directions; direction unclear. | One-way network: every piece runs from end 0 to end 1; each switch splits or merges (`TrackNetwork.Orient`, a parity solve per connected part, keeping the old railway's direction and turning the new / linked part). Arrows are part of the track mesh; wrong-way joins are refused with a reason. Old saves are oriented on load; trains facing against the flow run round. |
| Trains bunching. | Routing per splitting switch: avoid dead-end spurs, fewer trains ahead, not the branch taken lately; circuits (strongly connected parts) - only cross into a part trains can't leave when it has fewer trains per metre; headway hold at stations. Tested: three trains on two linked loops, 5 min: 0 reversals, 48 % of train-time in the quieter loop. |
| Guidance. | Tips (amber banner, contextual, each twice, Tips button), messages in a readable banner. |
| Variety. | Express (diesel) and tank engine; country halt. |

Validation for this round: 323/323 journey checks, 43/43 controller checks, `OneWayJourney`, `LoopJoinProbe`,
`editor-rail-oneway.png`, `editor-rail-express.png`, `editor-rail-tank-halt.png`, `editor-rail-plan.png`. On the
player's World 1 the railway orients one way (5 circuits, 1 trap, which the tip points out).

Limitations: a single line with two dead ends still shuttles (trains reverse at its buffers); dead-end spurs are kept off
by trains; the tip and preview explain both.

## Town buildings that say what they are

Reviewed side by side from the front (`editor-town-shops-1/2.png`, night: `editor-town-shops-night-1/2.png`): the bakery,
café, grocer and restaurant were the same house with a different awning; the disco, police station and studio were
plain boxes. Now: a 5 x 7 block-letter font built from merged boxes (`Lettering`), name boards on the roof's front edge
(letters glow at half strength after dark), symbols of each trade (loaf, cup with steam, apple, plate and cutlery, ON AIR
lamp, FUEL pylon), and the disco's neon sign: dark glass tubes in the building mesh with five glowing letter parts that
`BuildingSystem` switches in a 7-second cycle (chase, blink, shine). Added after the hand-made warp, so lettering stays
straight. Tested: the neon passes through 6 distinct lit states over its cycle.

## Railway building restored; GPU load cut

**Railway building blocked (regression).** Three of the player's four worlds held an old railway that turns back on
itself (a loop and a line meeting at one switch, joined the wrong way, from before the railway ran one way). Building
oriented the *whole* network and refused when any part couldn't run one way - so every new piece, even a loop in open
land, was refused ("trains would have to turn round"), while the preview (which skipped that check for loops) showed
green. Now each connected part is oriented on its own: a part that can't run one way stays two-way (no arrows, trains
shuttle there), and a new piece is refused only if it *creates* a new turn-back. Preview and build use the same test.
`WonderlandTrackProbe` on the four headset worlds: a loop in open land builds on all four; branches 10/12 on the old
worlds (the 2 refusals: too close to track / a switch); green previews always built. Regression test in the rail journey.

**Grabbing track.** The drag works through the controller path (new controller check); on the headset it gave no hint
where to grip and its refusals were in the tiny old status line. Now the grab point is ringed on hover, the hint says
"GRIP and move: bend this piece of track", refusals show in the banner, and grabs are logged.

**GPU load** (the player's current world: 59 buildings, 93 trees, 75 people; headset ~39 fps, CPU main ~6.5 ms, so
GPU-bound). `WonderlandTrackProbe.Budget`: 976 k triangles drawn + 703 k for shadows. Changes: simple stand-ins
(`MeshProxy`, built from each mesh's own area-weighted colours, with the name board) cast the shadows of buildings and
trees and are drawn instead of them beyond 2.2 m; ground cover 30 % thinner and not drawn beyond 2.6 m; clouds a
quarter of the triangles. Shadow triangles 703 k -> 266 k; buildings beyond 2.2 m: 47 stand-ins = 850 triangles
(instead of ~190 k). `editor-standins.png` shows full models beside their stand-ins. Not yet measured on the headset.

## Roads, junctions, traffic lights and placeable vehicles

**Roads drawn like track.** A Road or the new Highway is planned by simplifying the stroke (Douglas-Peucker, 1.8 cm
tolerance for a street, 3 cm for a highway), merging corners closer than 4 cm, and rounding each corner with an arc of
5 cm (street) or 18 cm (highway) radius, capped so it fits between neighbouring corners. Ends snap onto other roads.
While drawing, the planned road is previewed (re-planned every 0.15 s). Lanes, paths and trails keep the smoothed
free-hand stroke.

**Looks.** A street is 6 cm wide: asphalt between raised kerbs, white edge lines and a dashed centre line. A highway is
9.2 cm wide: four lanes, a double yellow line and dashed lane lines. Street lamps stand along streets. Markings are
left out inside junctions.

**Junctions** are found where car ways cross (segment intersection through the spatial index) or where one ends on
another. A junction is kept when it has three or more arms. Each gets a round asphalt patch, a stop line across its
incoming lanes, and a traffic light on the kerb of every arm.

**Lights** (`TrafficSystem.Lights.cs`): arms facing each other share a phase. The cycle is green 6 s, amber 1.6 s, all
red 1.2 s, offset per junction. The lamps are one dynamic mesh, rebuilt only when a light changes.

**Vehicles obeying them:**
- A vehicle brakes to stop with its bumper at the stop line, and stops on amber if it can.
- Once its nose is over the line it clears the junction. Sirens go through.
- Waiting at a light, or in the queue behind one, never triggers the old "edge on after 4 s" deadlock breaker.

**Vehicle sensing.** A vehicle now senses the whole body of the one ahead, tail to nose, so a long bus turning across
the lane still counts. When two vehicles block each other (crossing paths), one of them goes first.

**Placeable vehicles:**
- New models: taxi (with a TAXI sign and chequers), van, bus, lorry and tanker.
- They are placed from the Roads tool's second row and roam: on along the road they face, a random way onward at each
  junction, never doubling back except at a dead end.
- They keep to their lane for the road kind; buses and lorries take a highway's slow lane, and highways are 1.5x faster.
- They are saved (`cars` in the save and in undo snapshots) and erasable.

**Tests:**
- New road journey (`scripts/unity.sh roads`): straightness, rounded corners, a crossroads and a T-junction with 7
  lights, a highway, seven vehicles of six kinds over 3 minutes. Every vehicle moved at least 6 m; 168 stops at
  lights, 0 red crossings; at most 1.8 cm from a road's centre; never closer than 1.5 cm; save/load; erase and undo.
- Controller test: the Road chip lays a road, and the Bus chip puts a bus on it.
- Renders: `editor-roads.png`, `editor-roads-crossroads.png`, `editor-roads-highway.png`.

**Old saves.** Roads drawn free-hand before this change keep their points. They now render as asphalt with markings,
but stay as wobbly as they were drawn. Redraw them to straighten them.

## Performance: measured on the headset, then fixed where it costs

**Measurement.** `PerfBench` runs on the headset, on saved worlds in a fixed view, with the clocks held. It switches
one thing off at a time and reads the headset's own app GPU and CPU time; Unity's GPU timing reads 0 on the Quest.

| | World 1 (126 houses, 219 people) | World 4 (80 houses, 83 people) |
|---|---|---|
| Frame rate | 43–45 fps | 56–59 fps |
| GPU time per frame | 18.4 ms, 98% busy | 13.8 ms |

The GPU was the bottleneck: at 72 fps the budget is 13.9 ms. Savings when each part was switched off, World 1 /
World 4:

| Switched off | Saving |
|---|---|
| Terrain shading | 5.1 / 3.0 ms |
| MSAA 4x | 4.0 / 3.1 ms |
| Villagers and animals | 3.4 / 1.6 ms |
| Vehicles | 2.1 / 1.1 ms |
| Track, roads and water | 1.9 / 1.0 ms |
| Ground cover | 1.4 / 1.4 ms |
| Shadows | 0.3 ms |
| Trees and buildings | about 0, behind the stand-ins |
| Stand-ins removed | +3.2 / +1.3 ms |

The CPU main thread was 12.6 ms on World 1, of which the simulation was 5–7 ms.

**What changed**, each unit committed separately:
1. **Performance panel** (Stats > Performance): live headset numbers and a switch for every setting, applied at once and
   remembered, plus Measure, the same sweep run in the headset on the current view. The blocky stand-ins are no
   longer drawn; they only cast the shadows.
2. **Terrain shading.** Light, the default, reads the five noise layers from a generated tileable texture instead of
   computing 20 hashes per pixel, with the same look (`editor-terrain-*.png`). Simple drops the noise and the painted
   flowers.
3. **Smart scheduling** for villagers and animals:
   - Engine positions are found once per frame instead of per villager per train.
   - The ground height is looked up again only after moving.
   - Walkability checks and obstacle scans are spread over frames; the next step is always checked.
   - Animals cache their ground while grazing and check fences every fourth frame, looking far enough ahead.
   - Figures beyond drawing distance are posed only now and then.
   - Editor timing on World 1: 6.6 ms down to 2.7 ms.
4. **Levels of detail that stay recognisable**, not stand-ins. The mesh builder builds at detail 1 or 2: features under
   2 mm or 3.5 mm and thin sticks are left out, rounded shapes get fewer facets, roof tiles become a strip per row,
   flower boxes bands of colour, and lettering stays at level 1. Figures use 0.4 times the sizes. Buildings and forest
   chunks switch at 1.3 m and 2.6 m; figures swap their part meshes. Triangles kept at level 1 / level 2:

   | | Level 1 | Level 2 |
   |---|---|---|
   | Buildings | 60% | 40% |
   | Trees | 30% | 29% |
   | Figures | 43% | 30% |

   Renders: `editor-levels.png`, `editor-levels-figures.png`.
5. **One-mesh villagers: deferred.** Merging each villager's parts into one draw means rewriting how figures are posed.
   It waits for a headset measurement with levels of detail and Smart scheduling in place, both of which cut the
   villagers' cost.
6. **Roads look right:**
   - The mesh follows a smooth curve, about 3° per step on bends.
   - Each cross-section is flat at the highest ground across the road, with a skirt under each kerb, and the ground
     under a road is levelled, so no terrain shows through.
   - Markings are continuous strips, with dashes measured exactly along the road.
   - Render: `editor-roads-bend.png`.
7. **Building grid**, on by default, with a Grid chip to turn it off:
   - Road corners land on a 5 cm grid.
   - Buildings face the nearest road and line up along it; elsewhere they sit on the grid in quarter turns.
   - The grid shows around the pointer while placing.
   - Render: `editor-grid.png`.

**Second round** (after the first headset session):
- Five levels of detail chosen by how big things look on screen (Extreme up close, then High, Medium, Low,
  Minimal).
- Distance haze.
- A World size setting.
- Coarse simulation for villagers and animals behind the player or beyond 3 m. Editor timing on World 1, with a player
  at the edge of the land: 7.1 ms every frame, 2.0 ms Smart.
- Villagers and animals drawn together (GPU instancing, one draw per part mesh).

Known: the "Clouds drift on the breeze" check failed once and passed on rerun; it looks flaky, and is unrelated.

**Not yet re-measured on the headset with all of this in place.** Run Measure in Stats > Performance, or the
benchmark ("bench" file), to see the new numbers.

## Selecting anything, and following a villager's day

**Selection:** one gesture everywhere. Grip on whatever the gold ring is under opens its card, in any tool.
- **Targeting** (`RayPick`) scores how close the pointer passes to each figure's upright axis, with a tolerance that grows
  with distance (5 mm plus about 1 degree). That makes 3 cm villagers easy to hit, and a villager in front of a building
  wins over the building.
- **Hiding** comes from the land and the lower 55% of a building's box (its walls; roofs taper), with 3 cm of grace for
  someone at a door.
- **Trains and cars** are now selectable too, with simple cards.
- **The ring** under the target and the gold pointer show what grip would select.
- **Tips** appear the first time you point at a villager or a building, train or car.
- **Construction:** Build, Track and Train keep grip for their own actions, and nothing is picked while drawing or
  carrying.

**The villager's camera** reuses the CCTV system: the same hidden camera, the 320×240 viewer texture at 15 Hz, and the
monitor over the palette. Following takes precedence over the Watch tool's selection, and choosing a camera in the
Watch tool hands the viewer back to it.
- **On foot:** over the shoulder at eye level (16 mm behind and 6 mm above the head). Inside the eyes, someone standing
  and talking filled the picture.
- **On a train:** outside the first carriage's window, looking out and slightly ahead.
- **In a car:** the front passenger window.
- **Indoors:** a building view from the front, a little above. If that spot is inside another building it moves round
  in 45° steps until it isn't.
- **Smoothing:** glides within a view, and cuts between views.

**Cars:** villagers didn't ride in cars before. When a household car sets off, a resident who is at home indoors
becomes its driver, and the camera rides along. The simulation is not changed: if they come out before the car is
back, the link drops (checked once a second). An earlier version kept the driver indoors for the trip, which emptied the
restaurant at supper; the town test caught it.

**Performance:** only one feed is filmed. Figures within a metre of the follow camera are drawn and kept moving smoothly
even out of the player's sight; everything else keeps the usual savings.

**Validation:**
- `scripts/unity.sh followrender` on Worlds 1 and 4 finds villagers on foot, indoors, on trains and driving, follows
  each, checks the camera (near their eyes on foot, above ground, never inside another building) and records the views
  they pass through. For example: outside the cottage, then over their shoulder, then outside the cottage again; and
  from the car, then over their shoulder. 7 checks pass.
- Pictures: `editor-follow-{shoulder,window,car,outside}.png`.
- New controller checks:
  - selecting a villager turns on their camera and the monitor;
  - the Camera button turns it off;
  - the gold ring is under the target;
  - grip on another villager moves the camera to them;
  - grip on a train opens its card, and its camera rides along;
  - Close puts the camera away.

## Room to move, trains by the rules, a Select tool

**Villagers** (`scripts/unity.sh walkprobe`: two minutes on Worlds 1 and 4, old walking vs new):

| | Before | After |
|---|---|---|
| Spins (turning three-quarters round within about a second), World 1 | 12–44 per run | 3 |
| Walkers within 5 mm, per frame, World 1 | 1.5–2.5 | 0.55 |
| Walkers within 5 mm, per frame, World 4 | 0.5–2.3 | 0.01 |
| People inside a car's body, per frame, World 4 | 0.2–0.3 | 0.00 |

- **The twirl** was hikers circling a trail point they couldn't quite reach. A waypoint they stop getting closer to is
  now skipped.
- **Routes** are softened by Chaikin corner cutting, and gentle corners are rounded by aiming at the next waypoint.
- **Personal space:** villagers steer round each other and round cars, and wait for someone or a passing car rather
  than walk through. They don't wait for room near a door or seat, or on a doorstep.
- **Umbrellas** stay down under a station canopy or a roof.

**Trains** (`scripts/unity.sh trainflow`: five minutes on four of the player's worlds):
- Every piece of track has a direction, including old parts that can't run one way everywhere; their wrong-way
  branch is never taken.
- No reversing, no turning back, no engine jumping to the other end. Turn round is gone. Measured: 0 reversals.
- Each switch's clearance is measured from its geometry (the branches 3.6 cm apart), not a fixed 16 cm. Side by
  side, trains now keep about 3 cm apart; before, 1.5 cm on World 4. A following train keeps 2.5 cm.

**Track connections** (`scripts/unity.sh trackshape`, 24 hand-drawn connections; `trackshaperender`):
- Strokes are de-wobbled (Douglas-Peucker to 1.5 cm), and the one- or two-curve fit is preferred for connections.
- Spline overshoot next to a switch is smoothed out.
- Short wiggles went from 7 to 6, and turning beyond what a piece needs from 89° to 82° each: a small gain. The
  renders look smooth before and after, so a reported case is needed to go further.

**Select tool** (replaces Watch):
- The trigger selects anything with the gold ring under it; grip still works in every tool.
- A selected CCTV camera shows its picture on the monitor, with ‹ › ^ v buttons round it to turn and tilt it. The
  stick also turns it, and the trigger on the land points it there.
- The Cam and Train chips open each camera.
- The monitor is shown only while something is being watched.

## Known defects and risks

- **Station, water, bridges, tunnels, animals and villagers are untested on the device.** They are covered by the
  automated journey and editor renders only.
- Villagers decide only from what is near and reachable now. Train journeys use the nearest station on each side. A
  passenger boards only a train that will call at their station, as the switches are set now; in Random switch mode
  (or Explore, with other trains about) that can change, and they get off and change trains. A train that comes back
  from a dead-end branch runs round a loop the way that can't take the branch again, so a branch station may only be
  served now and then.
- Produce is a single kind of crate: wheat can end up at the café. Shops are restocked by the van even without farms.
- Vehicles belong to households, not to particular villagers: a car's trip isn't a villager's trip. Vehicles pass
  through each other at a junction if both arrive at once from different sides (there are no give-way rules beyond
  "slow behind whoever is ahead in your lane").
- Smoke and the fire hose are particle systems: they don't show in the editor renders (no simulation outside play
  mode), only in the app.
- The disco, the studio and the factory are plain on their backs; place them facing the road.
- The walk grid's 2.5 cm cells can miss gaps narrower than about 3 cm between buildings, so such a gap counts as
  blocked.
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

Built 2026-11-16: `Builds/Android/Wonderland.apk`, 58.8 MB, sha256 `1af0a91e09ac4a08…`, installed on the Quest, package `com.splink.wonderland`
0.1.0 (versionCode 1), minSdk 32 / targetSdk 34. Shader-error check clean. The APK manifest declares USE_SCENE,
USE_ANCHOR_API, BOUNDARY_VISIBILITY, HEADSET_CAMERA and INTERNET (the last added by Unity/Meta SDK defaults).

## Effort and cost

- About 1.5 hours of autonomous agent work from the copied project settings to the APK.
- About 10,300 lines of C#, HLSL and ShaderLab.
- No paid services, APIs or assets; everything was built and rendered locally (Unity batch mode on an AMD laptop GPU).

## Next steps (headset session)

1. Wear the headset with the app running for a minute of heavy use (sculpting next to a forest with the train and
   villagers out), then read the `Wonderland perf` lines, or capture a trace with `metavr perf capture`. Also: a
   village of 40 on **Fast days** through a whole day and night, to check the villager simulation, the grid rebuild
   after edits, and the night lighting through passthrough. Call a fête, and check that the farmer's crate and the
   baker in the doorway read at arm's length.
2. Try the new tools: dig a pond and draw a stream, and run the railway over them; raise a big hill across the line for
   a tunnel; place a station and a few villagers next to it, and watch them board.
3. Check legibility of the 3 cm villagers and the water's look through passthrough.
4. If draw calls show up: instance the animals and villagers.
5. The livelier town on the headset: build a town along two roads with a hospital and a fire station, put up two
   cameras and a studio. Then watch with the Watch tool open: is the frame rate steady while the feed is filmed? The
   `Wonderland perf` line counts the pictures filmed and the sounds playing. Listen with Sound on and Quiet.
6. Natural next improvements: villagers using their household's car for long trips; give-way at junctions without
   lights, and bus stops on the bus's round; straightening roads in old saves; more incident scenes on the same dispatch (a cat up a tree, a broken-down car); a second CCTV
   viewer pinned in the room; puddles and umbrellas in the rain.
