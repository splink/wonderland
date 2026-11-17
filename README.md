# Wonderland — a mixed-reality miniature world for Quest 3

Grow a storybook miniature landscape out of your real living-room floor (passthrough), then **paint, draw, place**:
sculpt rolling hills, dig ponds and draw streams, paint forests, draw a railway loop (it bridges water and tunnels
through hills), put down houses and a station, let sheep, cows and villagers loose, and watch a little steam train run.
The village keeps its own day: shoppers in the morning, the fair in the afternoon, lit windows in the evening, and
quiet nights. Villagers have jobs, grow and carry produce (across the line by goods train), make friends and come
to a fête when you call one. What you build decides where its people go.

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
| Right stick ← → | brush size (Build: flick to turn 15°, hold to turn smoothly; the arrow shows the front) |
| Right stick ↑ ↓ | strength (Forest: density) |
| A | next mode: Raise → Lower → Smooth → Flatten → Grow → Trim → Peaks · next path kind · next item · Pond → Stream → Drain · Paint ↔ Erase · next house · Sheep → Cows → Villagers · Track: draw ↔ signals · Trains: start/stop (the one pointed at, or all) |
| B | undo |
| Right grip | on a villager or animal (any tool): open their card · pick up a house, lift a train, or bend a piece of track |
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

**Roads** (the tool's chips: Road, Highway, Lane, Path, Trail, Fence, Hedge, Wall; and a second row of vehicles).
Streets and highways, country lanes, footpaths and hiking trails, plus fences, hedges and stone walls, all drawn with one
stroke:
- **Streets and highways come out straight**, like track: the stroke is straightened, with rounded corners (a highway's
  are wider). While you draw, a line shows what letting go will build.
- A street is asphalt between kerbs, with white edge lines and a dashed centre line. A highway has two lanes each way
  and a double yellow line.
- **Junctions:** draw across a road for a crossroads, or end on one for a T-junction. Each gets a patch of plain
  asphalt, stop lines and, where three or more roads meet, traffic lights on every arm.
- **Cars and lorries:** pick Car, Taxi, Van, Bus, Lorry or Tanker and pull the trigger on a road. It drives off
  wherever it is pointed and roams the town. It keeps to its lane (buses and lorries take a highway's slow lane), keeps
  its distance and stops at red lights. Vehicles are saved with the world; the Eraser takes one away.
- Lanes, paths and trails keep the free-hand look.
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
- **A day in the village.** The village has a clock: a day passes in under eight minutes. The sun climbs from the
  east and sets golden in the west, and nights are moonlit. Choose **Days pass**, **Fast days** or **Stop time** in the
  Life tool's second row.
- **Villagers keep a routine.** Each has a home and a few needs that grow through the day: shopping, food, company,
  fun, sights and rest. In the morning they go to the bakery and the grocer, at lunchtime to the café, in the afternoon
  to the fair, and in the evening to the fountain and the church steps.
  - Between outings they pop home for a while. In the evening they only go as far as they can get back from by
    bedtime.
  - At bedtime they head home and go inside. Windows glow where someone is in, go dark where nobody is, and go out after
    midnight. The rides stop for the night, and animals settle down.
- **Opening hours and jobs.** Shops, the café, the fair and the school keep opening hours: nobody shops after the
  bakery shuts at six, and the rides stop in the evening. Fifteen kinds of place come with a job: the baker, grocer,
  farmer, gardener, miller, teacher, station master and others.
  - Each job goes to the nearest willing villager, closest first. Whoever lives over a shop keeps it, and others walk
    to work or commute by train. The palette announces new hires ("Ida is the new farmer at the wheat field").
  - Workers keep their shifts. Shopkeepers stand in their doorway, the farmer hoes at the edge of the field, and the
    teacher works indoors with the lights on.
- **Produce.** Farms (wheat field, garden, greenhouse, barn, windmill) grow crates of produce while their farmer works.
  - Workers carry the crates by hand to the bakery, grocer and café on their side of the line: the farmer delivers,
    and a shopkeeper with emptying shelves goes and fetches some.
  - Every visit takes a little off the shelves. Bare shelves draw fewer people, and the village says so ("The
    shelves at the grocer are bare - a farm nearby would help").
  - Across a railway, crates go by goods train. The farmer takes them to the station, the next goods train loads
    them, and it unloads where shops want them. The shopkeeper there fetches them from the platform.
  - A van from beyond the village tops shops up slowly, so a village without farms still has bread, just less of it.
- **Friends.** Villagers who spend time at the same place (the café tables, the fountain, the fair) become friends,
  and the palette tells you. Friends seek each other out: a place where a friend is draws more, and calling on a friend
  brings them out to the door for a chat. Friendships are saved.
- **Fête.** Tap **Fête** in the Life tool and the village gathers round its main square (a fountain, bandstand or
  well) for a couple of hours. Tap it again to call it off.
- **Trains by destination.** Passengers board only a train whose route reaches their station, following the switches
  as they are set. If a switch sends their train elsewhere, they get off and change.
- **What you build changes what they do:**
  - A new café draws its first visitors within moments (the palette tells you who).
  - Removing a place sends people elsewhere.
  - New homes bring more people to the shops nearby.
  - Every place has a set number of spots, so it never gets overcrowded.
- **Where they can get to matters.** Villagers walk along roads and paths and go round fences, ponds and buildings.
  They only set off for places they can actually reach.
  - Someone living inside a railway loop stays inside until you add a level crossing, or stations and a passenger
    train. With stations they ride across and walk on from the far platform.
  - Homeless villagers move into a nearby house with a spare room, or sleep under the stars.
- **Point at a villager** (Life tool) to see who they are and what they're doing ("Greta - browsing at the bakery (the
  morning shopping)", "Ben - carrying vegetables to the grocer", "Kit - sitting at the café with Ida"). Point at a
  building (Build tool) to see who's there, who works there, its opening hours and how full its shelves are.
- **Animals** each keep to their habitat. Grazers stay on grass and in their pasture, hens peck about the yard, ducks
  paddle on ponds, and deer keep to the woods and bolt when someone comes near.

**A livelier town.** Everything below joins in the same daily life:
- **New places.** A park (benches, a duck pond, trees), a museum with a dinosaur in its forecourt, a restaurant with
  a terrace (busiest at supper), a disco that opens at eight and dances past midnight (a mirror ball turns on the
  roof), a hospital, a police station, a biscuit factory (its chimney smokes while someone works there), a football
  pitch, a monitoring studio, CCTV cameras and a hot-air balloon. The services are in a new **Services** category.
- **Children and night owls.** About a fifth of the villagers are children: smaller, at school in the morning, and
  keen on the playground and the pitch. A quarter of the grown-ups are night owls and keep the disco busy.
- **Football.** Whenever players are on the pitch there is a match: the ball is passed about, now and then shot at a
  goal, and the players run after it. Goals get a cheer.
- **Traffic.** Homes beside a road or lane get a car, a motorbike or a bicycle. By day they drive to the shops, the
  restaurant, the park or work, park at the kerb a while and come home, keeping to the right.
  - They slow down behind each other, stop for people on the road, and wait at level crossings while a train passes.
  - Bicycles may use footpaths. At night everything is parked.
  - Take a road away and the traffic re-routes or goes home.
  - At traffic lights they stop with their bumper at the stop line on red (and on amber if they can stop in time). Roads
    facing each other share a green, and every change has a moment of all red.
- **Emergencies.** A hospital, police station or fire station beside a road keeps an ambulance, a patrol car or a
  fire engine. Now and then (by day, one at a time) something happens: someone feels faint, smoke rises from a roof,
  a bicycle goes missing.
  - The vehicle sets off with blue lights and a siren, deals with it (the paramedics help the patient up, the hose
    puts out the smoke) and drives back.
  - If it can't get through, the incident ends quietly.
  - The Life tool's **Emergency** chip calls one at once.
- **Dogs and cats** (Life tool). A dog lives with the nearest household: it pootles about the garden, and when its
  owner goes out it trots along in their footsteps (never through a house or over a fence). It waits outside the shop
  for them, or goes home the way it came. A cat keeps to its territory: naps, strolls, sits up on walls and fences,
  and keeps out of a dog's way.
- **Sky.** A few clouds drift over the land and cast slow shadows (the Life tool's **Clouds** chip hides them).
  Hot-air balloons take off from their launch field by day, float over the countryside and land again.
- **Sound.** All of it is generated by the app, with no recordings:
  - birds over the woods and water at ponds and streams;
  - steam chuffs, whistles as trains leave and brakes as they stop;
  - engines, sirens, the fair's organ and the disco's beat;
  - chatter at a busy café, children at play, the factory at work;
  - barks, meows, kicks and cheers.

  Only the eight most audible nearby sources play, so it stays calm. The **Sound** button at the bottom of the
  palette cycles On / Quiet / Off, and the setting is remembered.
- **CCTV.** Put up cameras (Build ▸ Small ▸ Camera; a camera looks the way its front faces) and open the
  **Watch** tool.
  - A little monitor above the palette shows the selected camera live: the trains, traffic and people as they move.
  - Tap a camera to watch it, tap the land to point the watched camera there, or press A for the next one.
  - A monitoring studio shows four feeds on its front wall, and villagers stop to watch.
  - Cameras keep their aim through edits, saves and loads.
- **Point at a vehicle** (Life tool) to see what it is doing ("The ambulance - on a call, lights flashing").

**Lives you can follow.**
- **Day plans.** Every villager plans their day on waking: the morning shopping at their usual shop, their shift
  (with a lunch break) or school, an afternoon out (the park, the fair, a hike if they live by a trail), an evening
  out, home by bedtime.
  - Their favourite places stay the same from day to day.
  - A trip to another village goes by train.
  - If a planned place is taken away, closes or can't be reached, that part of the plan is dropped, and somewhere
    they failed to reach is left alone for a few hours.
- **No aimless wandering.** Nobody with a home wanders about. Villagers without a home keep to their own patch and look
  for a spare bed within a walk; homes have room for a family. Only a few walkers are out on the trails at once.
- **Character cards.** In the Life tool, pull the trigger on a villager (or an animal) for their card: a little
  turning model of them, their name and job, a few lasting traits that shape what they do, their home, their work or
  school, what they are doing now, where they are going and why, what comes next, and their friends.
  - A marker floats over them, a line shows the way they are walking, and pins mark their home and work.
  - Pets and farm animals have names and say whose they are and what they're up to; wild deer and ducks stay
    nameless.

**A reliable railway.**
- **Drawing track:**
  - draw back to the start for a loop;
  - start on a track for a branch with a switch;
  - draw from a dead end to extend it;
  - end on a track to join it;
  - draw from one loop to another to link them.

  Connectors and short extensions that used to be refused ("curve too tight", "draw a longer track") are now fitted
  with a smooth curve. The message after each stroke says exactly what was built, or why not.
- **Signals are automatic:** red while a train is on the line beside them, green when it's clear. Tap one to hold
  trains at red, and tap again for automatic.
- **Carriages:** point at a train with the Train tool and push the stick left or right to uncouple or couple
  carriages (wagons, railcar units). The engine stays put and the length is saved.
- **Status:** pointing at a train says what it is doing and why it waits ("waiting at a red signal", "waiting for the
  train ahead", "at the buffers, reversing") and its next stop. Stations are named after their villages
  ("Brookside").

**Always on the palette:** the village clock, with a light beside it for how the headset is keeping up: **green**
(full frame rate, room to build more), **orange** (frames starting to run late, close to the limit), **red** (falling
behind: the world is heavy for the headset). It is measured from the real frame time, not estimated.

**Who is that?** Point at anyone (or any building or boat) with any tool and the palette says who or what it is;
**grip** opens the card - in the Life tool the trigger does too. Cards are laid out in sections with fixed text sizes:
- **a villager:** Now (doing, going to, then), Today (their plan, the part under way highlighted), Home and work, About
  (character, when they get up and go to bed, friends); a marker follows them, a line shows their way;
- **a building:** open or closed (and its hours), visitors now and who they are, its staff and goods, visits today and
  yesterday and when it is busiest; for a home who lives there and the beds; for a station the trains calling next.

Switching tools closes the card. **Stats** and **Help** use the same layout (Headset, Simulation, World; This tool,
Everywhere).

**Weather.** Fourteen clouds drift on a slowly turning wind and wrap round the land. Now and then (about one shower in
ten village hours) they turn grey and it rains: drops fall from the clouds over the land, the light dims, rain can be
heard, villagers choose the museum or the café over the park, and whoever is out puts up an umbrella (each a moment
apart). The Life tool's **Rain** chip starts or stops a shower.

**Boats and landing stages.** Build > Travel has a **Landing stage** (put it at a pond's edge, its end out over the
water) and three boats to put on a pond: a **rowing boat** (its oars dip and sweep), a **sailing boat** (the sail swings
to the wind; fastest across it) and a **paddle steamer** (six passengers). Boats keep to their own water and never run
aground. Villagers come to the landing stage for a boat trip; a boat comes alongside, they sit in it while it goes round
the lake, and step off when it brings them back. Point at a boat to see what it is doing; the eraser takes one away.

**Cameras on trains.** Every train carries a camera on the front of its engine: in the Watch tool its chips (Train 1,
Train 2...) follow the cameras on poles, giving a driver's view of the line. Turning a camera while placing it shows
the edges of what it will see; in the Watch tool the **stick turns and tilts** the camera being watched.

**Carriages:** in the Train tool, point at a train, then press **+ Carriage** or **- Carriage** on the palette (or push
the stick left/right while pointing at it). **Turn round** moves the engine to the other end of its train, so a train
that was running backwards runs engine first. Trains also do this themselves at the buffers, and at the next station
after backing away from another train.

**The world stays where you put it.** It is pinned to the room with a Meta spatial anchor: next launch it comes back to
the same spot, and it doesn't drift when the headset re-centres (taken off and put on again). Its height is the room
scan's floor. **Move** re-pins it.

**A one-way railway.** The whole railway runs one way: cream arrows between the rails show which way, and every train
runs with them, engine first - trains can never meet head-on. A switch either splits one line into two going the same
way, or merges two into one. When you add track, the new line takes the direction of the track it joins, and a loop
you link up turns round to match; a join that would make trains reverse (a line turning back into itself the wrong way)
is refused, saying so.

**Drawing track: you see the result before you let go.** While the trigger is held the planner runs on what you have
drawn so far, exactly as letting go would: the line it would build shows **green, with arrows** the way trains will run
on it, or **red** with the reason in the banner above the palette ("a curve is too tight", "tracks must cross at a wider
angle"...). When a link would leave a track too sharply, the new switch slides a little along the track to give the
curve room. Two loops joined by one link let trains across but not back - the preview and a tip say so, and suggest a
second link the other way.

**Trains spread out.** At each splitting switch a train takes the way with fewer trains ahead (and the way taken less
lately), keeps off dead-end spurs (it couldn't come back out), and only crosses into a part it can't return from when
that part has fewer trains for its length; at stations a train waits a little if the one ahead is close. **Switches:
Spread** (default), Random or Manual.

**More trains and stations.** The Train tool's second row picks the train: **Steam**, **Express** (a streamlined diesel
with four carriages, faster), **Tank engine** (small, slower, two carriages), **Goods** and **Railcar**. Besides the
station there is a **Country halt** (Build > Travel): a little shelter with a shorter platform; trains call there too.

**Town buildings say what they are.** Each one carries its name on a board standing on its roof's front edge (BAKERY,
CAFE, GROCER, RESTAURANT, MUSEUM, TV STUDIO, HOSPITAL, POLICE, FIRE STATION, TOWN HALL, SCHOOL, BISCUITS on the factory,
FUEL on a pylon) - real lettering that glows softly at night - and the shops a big symbol of their trade: a golden loaf
on the bakery, a steaming cup on the café, a giant apple at the grocer's, a plate with knife and fork on the restaurant;
the studio has an ON AIR light. The **disco** has big multicoloured neon letters in a chrome frame that light up one by
one, blink, then all shine.

**Tips.** Now and then an amber banner above the palette suggests what to try or explains what just happened - each tip
at most twice, one at a time. The **Tips** button turns them off (and on again, starting over). Messages appear in a
banner above the palette too, two lines at a readable size.

**Streams between ponds** run downhill at the ponds' own water levels, whichever way you draw them, full width into
the ponds, without damming them; a stream from a higher pond falls into the lower one.

**Help, stats and worlds.**
- **Help** shows the controls of the tool in hand and the general buttons.
- **Stats** shows the time, the frame rate, CPU and GPU time where the headset reports them, what the simulation costs
  system by system, and what the world holds (people, animals, buildings and beds, trains and track, vehicles,
  cameras).
- **Redo** sits next to Undo (B on the controller also undoes).
- **Save** and **Load** keep up to six worlds, World 1 to World 6. The world in play is shown in the palette's title
  and keeps itself up to date when you leave the app.

**Mountains.** The **Peaks** terrain mode raises craggy summits. High ground turns to scree and rock bands, with snow on
the tops and boulders on the slopes. Ponds get sandy beaches.

Palette: **Terrain · Water · Forest · Roads · Track · Train · Build · Life · Watch · Eraser** tools, an options row for the current
tool, and **Undo · Redo · Save · Load · Move · New · Sound · Help · Stats**. The world saves automatically when the app is paused or closed and is restored
on the next launch.

## How it is built

Everything you see is generated at runtime by C# from seeded procedural generators sharing one palette and a few
shaders: no downloaded or purchased models, textures or services.

| Area | Code |
|---|---|
| Mesh toolkit, noise, palette | `Assets/Wonderland/Scripts/Core/` |
| Trees (5 kinds, 17 variants), meadow plants, houses (cottage, townhouse, thatched, station × 4 styles), sheep, cows, villagers (6 outfits), locomotives, coaches and railcars in 6 liveries, 4 goods wagons | `Assets/Wonderland/Scripts/Assets/` |
| Terrain field + renderer, water, streams, forest, ground cover, railway network (graph, planner, mesh with switches, signals, bridges, tunnels), trains (reservations, routing), buildings + platforms, animals, villagers, world (undo / save) | `Assets/Wonderland/Scripts/World/` |
| Village life: the clock (`VillageClock`), needs and their daily rhythm, and intents (`Needs`), where people can walk (`WalkGrid`: regions + A* routes), choosing (`PeopleSystem.Life.cs`), jobs and produce (`PeopleSystem.Work.cs`), friendships (`PeopleSystem.Friends.cs`), plans from outside and the fête (`PeopleSystem.Plans.cs`: `IVillageDecider`), carrying out (`PeopleSystem.cs`); opening hours, jobs and goods per place in `Catalog` | `Assets/Wonderland/Scripts/World/` |
| The livelier town: football and mishaps (`PeopleSystem.Games.cs`), road traffic on a graph derived from the roads (`TrafficSystem.cs`; traffic lights and the player's roaming vehicles in `TrafficSystem.Lights.cs`; roads straightened, junctions and markings in `PathSystem.Roads.cs`) and its emergencies (`TrafficSystem.Incidents.cs`), pets (`AnimalSystem`), spatial sound with a fixed voice pool (`SoundSystem.cs`; clips synthesised in `Core/SoundBank.cs`), CCTV filmed on demand (`CctvSystem.cs`), clouds (`SkySystem.cs`); new places, vehicles, a dog and a cat in `HouseFactory.Town.cs`, `VehicleFactory.cs`, `AnimalFactory.cs` | `Assets/Wonderland/Scripts/` |
| Palette UI, controller input, tools | `Assets/Wonderland/Scripts/Interaction/` |
| Boot: permission, MRUK floor + free space, placement, autosave, foveation; frame-timing log (`PerfLog`) | `Assets/Wonderland/Scripts/App/` |
| Shaders: `Wonderland/Lit` (vertex-coloured storybook shading, wind sway), `Wonderland/Terrain` (procedural grass, flowers, soil, rim fade, brush ring), `Wonderland/Water` (depth tint, ripples, glints, shore foam, flow), UI, steam | `Assets/Wonderland/Shaders/` |
| Scene + Quest configuration, build, tests, renders | `Assets/Wonderland/Editor/` |

The project settings were seeded from the EasyPiano Quest project (same Unity / Meta XR / URP stack); the
application is `com.splink.wonderland`.
