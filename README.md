# Testaria

Unit, integration, and gameplay testing for Terraria mods built on tModLoader.

**AI Disclosure** This mod is currently 99% authored by Claude Opus 5 and only 5% has been subject to human review. That is not the planned final state. The mod is published in this state for the purpose of demonstrating functionality and seeking implementation and compatibility feedback. A full release will involve full human review, refactoring and additional feature development, and likely architecture changes.

**Status: early alpha.** All four tiers run end to end in a live headless game, driven by the `testaria` command line tool, and the framework has been calibrated against ExampleMod and four other community mods. Tier 3 covers netcode, with a real client process joining a real server, and reaches far enough into a client to read back pixels it drew. Nothing is published to nuget.org or Steam Workshop yet.

## What does it do?

Some Terraria mods implement their own unit testing. A few load their mod into the game server to confirm it doesn't immediately crash. Almost none perform any significant integration testing. Testaria aims to fill a gap in the Terraria modding ecosystem by making it easy for any mod to implement one or more tiers of testing ranging from pure logic unit tests to "actually launch the game and perform some actions in a multiplayer world and measure the outcome over thousands of ticks".

Project design lives in [`PLAN.md`](PLAN.md).

## Getting started on your own mod

Set up the templates:

```sh
dotnet new install Testaria.Templates
dotnet new testaria-unit-tests -n MyUnitTests                  # tier 0, out-of-game tests
dotnet new testaria-mod-tests  -n MyModTests --subject MyMod   # tiers 1 to 3, in-game tests
```

Tier 0 is an ordinary xUnit project, so it runs wherever `dotnet` does:

```sh
cd MyUnitTests && dotnet test
```

Tiers 1 to 3 need the game. Building writes `MyModTests.tmod` straight into your tModLoader `Mods` folder:

```sh
cd MyModTests && dotnet build
```

Then launch tModLoader, enable **Testaria**, your own mod, and **MyModTests**, load any world, and run the command in chat:

```
/testaria run MyModTests
```

It replies with the counts, and writes a JUnit report to `<tModLoader save path>/Testaria/MyModTests.xml`.

The same command works on a server console without the leading slash, which is what the command line tool sends for you.

Tier 3 needs a second game process, so those tests run through the command line tool instead:

```
dotnet tool install --global Testaria.Tool
testaria run --mod MyMod --mod MyModTests --client --blank
```

On Linux a client also needs a framebuffer, which the harness starts for itself: install `Xvfb` or point `DISPLAY` at an existing fb. A run without `--client` needs neither.

Nothing else about the suite changes. `[NetTest]` methods live in the same `MyModTests` project as the tier 1 and 2 ones, and the harness enables the same mods on the client that it enabled on the server, so your own code is already present on both sides.

The generated project references only the `Testaria.Core` package, which is enough for the attributes, `Assert` and `Wait`, but not for `ClientLink`. That type touches Terraria's own types, so it lives in the Testaria mod assembly, as `TestContext` does. Reference the built `Testaria.dll` to reach either:

```xml
<Reference Include="Testaria">
  <HintPath>/path/to/ModSources/Testaria/bin/Debug/net10.0/Testaria.dll</HintPath>
  <Private>false</Private>
</Reference>
```

`Private=false` matters: the assembly is already present at run time, because `build.txt` names Testaria in `modReferences`, and a copy in your own output is something ModCompile would try to pack into your `.tmod`.

[Tier 3](#tier-3-a-server-with-a-client-attached) below covers both, with a worked test.

### Tiers

| Tier | Name | Runs in | Can test |
| --- | --- | --- | --- |
| 0 | Unit | a normal `dotnet test` host | Pure logic with no dependency on loader state |
| 1 | Loaded | tModLoader `-server`, no world | Content registration, recipes, ID sets, config, localization |
| 2 | World | tModLoader `-server`, world loaded | NPC AI over ticks, world gen, tile framing, drop tables |
| 3 | Multi-process | server plus client(s) | Netcode and sync: packet round trips, what each side believes |

Tests are marked with attributes, and discovery finds them by reflection.

| Attribute | What it marks |
| --- | --- |
| `[LoadedTest]` | A tier 1 test: needs a completed load pass, but no world |
| `[GameTest]` | A tier 2 test: needs a world and a tick loop, and is given a box of its own |
| `[NetTest]` | A tier 3 test: needs a client connected to the server, and is given a box of its own |
| `[FreshWorld]` | A test no box can isolate, which needs a freshly generated world instead |
| `[Case]` | One set of arguments for a parameterised test, reported as a case of its own |
| `[CaseSource]` | A member supplying a parameterised test's cases, read during discovery |
| `[RealTime]` | Runs a test at the vanilla 60 ticks per second instead of fast fowarding |
| `[StartPaused]` | Freezes the world as the test begins, for a test that steps ticks by hand |
| `[Seed]` | Pins a test's randomness to a particular seed, rather than the one derived from its name |
| `[RequiresLoadedGame]` | Declares that a member needs the game, so tier 0 code cannot reach it by accident |

## The tier 0 boundary

Tier 0 doesn't start the game and is reachable only for logic you have deliberately factored out into an ordinary class library that your mod references. Doing so will greatly improve your experience testing individual functions for behavior related to logic, arithmetic, data structure manipulation, etc. If you haven't done this abstraction, you will need to write tier 1 tests instead. All tier 1 tests can run on a single start of the game, so the overhead is persistent but minimal.

`Testaria.Core` includes a boundary analyzer that will surface the following violations:

| Rule | What it catches |
| --- | --- |
| `TSTA001` | `ContentSamples`, `ModContent`, `Lang`, `Language`, `ModLoader.Mods` and friends, and any `*ID.Sets` member, used where no game has loaded |
| `TSTA002` | A call to a `[RequiresLoadedGame]` member from a caller that has not declared the same |

The analyzer is disabled for any assembly that declares a type implementing `ILoadable` which guarantees a load. To turn it off deliberately, set `<TestariaLoaderStateAnalysis>false</TestariaLoaderStateAnalysis>`.

### Keeping the suite out of your mod's own build

Your tests belong in your mod repo, but not in your mod. The usual tModLoader layout puts your mod's `.csproj` at the root of its repository, where the SDK's default `**/*.cs` glob picks up everything underneath. You can exclude your tests in your mod's `.csproj`:

```xml
<ItemGroup>
  <Compile Remove="MyModTests\**" />
  <None Remove="MyModTests\**" />
  <AdditionalFiles Remove="MyModTests\**" />
</ItemGroup>
```

and in the mod's `build.txt`, or the suite's source ships inside your mod's `.tmod`:

```
buildIgnore = MyModTests\*
```

If your mod repository is laid out with an extra layer of structure, you can put the test suite beside your mod's folder instead of inside it, avoiding these issues.

## Running a suite without the game in front of you

`Testaria.Tool` is the headless harness: it provisions a throwaway tModLoader save directory, installs the mods you name, generates a world, starts a server, runs the suite, writes the report, and turns the result into an exit code.

```
dotnet tool install --global Testaria.Tool
testaria run --mod MyMod --mod MyModTests --blank --speed max
```

```
tml:      /home/you/.local/share/Steam/steamapps/common/tModLoader
mods:     Testaria MyMod MyModTests
scratch:  /tmp/testaria-3nbvqzkh.2ax
server:   pid 31337, log /tmp/testaria-3nbvqzkh.2ax/server.log
world ready after 5s
sending: testaria run Testaria
39 tests: 39 passed, 0 failed, 0 errored, 0 skipped
```

Nothing it does touches your own installation: the run lives entirely inside that scratch directory, which is deleted afterwards unless you pass `--keep-scratch`.

CI can read the `testaria` exit codes:
* **0** when every test passed or was skipped
* **1** when one failed, errored, or was blocked
* **2** when the harness could not run the tests at all

Common flags:

| Flag | What it does |
| --- | --- |
| `--mod <name\|path>` | A `.tmod` to install and enable, by name or by path. Repeatable, and the order is load order |
| `--project <dir>` | Build a mod project first, so a run cannot quietly test the last build |
| `--filter <regex>` | Narrow the run to matching tests |
| `--blank` | Generate Testaria's blank world, a deterministic stone-and-air substrate, rather than a real one |
| `--speed max` | [Fast forward](#realtime-testing) the whole run |
| `--seed <n>` | Shift every test's [seed](#seeds-and-reproducibility) |
| `--results <path>` | Copy the JUnit XML report somewhere CI will look |
| `--require <n>` | Fail unless at least n tests actually ran, which catches a suite that skipped everything. Defaults to 1, so a run in which nothing ran is already a failure; raise it for a suite whose subject is another mod, or pass 0 to allow it |
| `--measure` | Write a table of what each boxed test cost and used, beside the report, for [calibrating the arena](#boxes-and-what-they-cost) |
| `--client [n]` | Start client processes and join them to the server, so [tier 3](#tier-3-a-server-with-a-client-attached) can run |
| `--keep-scratch` | Keep the save directory, and say where it is |
| `--verbose` | Print the server's own log as it happens, for a run that will not start |

`testaria list` takes the same options and catalogues the tests without running any of them.

### From a build, rather than by hand

`Testaria.Sdk` is the same run wired into MSBuild, for a mod repository that wants `dotnet build` to be the only command anyone has to know. It carries the `testaria` tool inside itself, so there is no install step:

```xml
<ItemGroup>
  <PackageReference Include="Testaria.Sdk" Version="0.1.0-alpha.1" />
</ItemGroup>

<ItemGroup>
  <TestariaTestMod Include="MyModTests" />
  <TestariaModProject Include="../MyMod" />
</ItemGroup>
```

```
dotnet build -t:TestariaRun
```

A failing suite fails the build. Set `TestariaRunOnBuild` to true to hang it off every ordinary build instead; it is off by default because a run starts a game and takes tens of seconds. `TestariaSpeed`, `TestariaFilter`, `TestariaRunSeed`, `TestariaWorldSeed`, `TestariaBlankWorld`, `TestariaTimeout`, and `TestariaResults` map to the flags of the same name, and the report lands in `TestResults/`.

### Unit tests that need the game's types in scope

Tier 0's strongest protection is that `Testaria.Core` cannot reach loader state, because the game is not referenced at all. Some unit tests genuinely need the types anyway, to test a method that takes an `Item` or returns a `TagCompound`. `Testaria.Unit` enables that sort of test:

```xml
<PackageReference Include="Testaria.Unit" Version="0.1.0-alpha.1" />
```

It references the install's assemblies without packaging a `.tmod` (similar to tModLoader's analyzers), sets the language version the 1.4.5 line builds with, and brings `Testaria.Core` and [the boundary analyzer](#the-tier-0-boundary) with it.

Set `TML_PATH`, or pass `--tml`, if your tModLoader installation is somewhere other than the default Steam library.

Once the placeholder tests run successfully to confirm your installation, then you can replace them with tests of your mod.

## Tier 3: a server with a client attached

A netcode test needs two processes, and `--client` provides the second one:

```
testaria run --mod MyModTests --client --blank
```

The harness starts a server and client, waits for the client to join, and only then runs the suite. A `[NetTest]` body runs **on the server**, which owns the run, the arena, and the report; the client is a puppet that answers questions about what it can see.

```csharp
[NetTest(Band = Band.Cavern)]
public IEnumerator A_tile_the_server_places_reaches_the_client(ITestContext ctx)
{
    var box = (TestContext)ctx;
    int x = ctx.Interior.Left + 4, y = ctx.Interior.Top + 4;

    // A client only knows the world sections it has been sent, which are the
    // ones near its own player. A box in the cavern is nowhere near where a
    // client spawns, so it has to be given this ground first.
    ClientLink.SendSection(x, y);
    yield return Wait.Ticks(10);

    box.PlaceTile(4, 4, TileID.Stone);
    NetMessage.SendTileSquare(-1, x, y, 1);

    ClientLink.Request seen = ClientLink.AskTile(x, y);
    yield return Wait.Until(() => seen.Answered, "the client to report what it sees");

    Assert.Equal(TileID.Stone, seen.Value);
}
```

Every question is a handle you wait on, never a value you read: the answer is a packet, and it arrives some ticks later. `ClientLink.Ping()` is the smallest round trip there is, `ClientLink.AskTile(x, y)` asks what the client believes about a tile right now, and `ClientLink.AwaitSection(x, y)` waits for the client to have some information about that tile.

**A client being in the world does not mean the world has arrived.** Benchmarks suggest it can take five or more ticks after joining before a client sees the tiles in its spawn block.

If you ever need to know who sent a client a piece of the world, `--arg -testariatracenet` logs every section and tile square the server sends, with the call stack that produced it.

### What the harness provides

- **Seeds the client's configuration.** This avoids the startup dialogs for language selection, changelog, etc.
- **Provides a framebuffer.** A server is happy with `SDL_VIDEODRIVER=dummy`; a client started that way exits within five seconds. On Linux the harness starts `Xvfb` on a spare display and stops it afterwards.
- **Drives the join from inside the game.** The client creates a test character then connects.

### Asking the client about your own mod

`AskTile` and `AskNpc` are about vanilla state, because that is all this framework understands on its own. Your mod's synced state, which is the entire reason your mod has netcode, needs your code to look at it. That code is already on the client, because a test mod is loaded on both sides; it only needed a way to be reached.

Register a question during your load pass, which runs on both sides:

```csharp
public sealed class ClientQueries : ModSystem
{
    public override void Load()
        => ClientQuery.Register("MyMod.SeesWidget", (arguments, answer) => {
            int x = arguments.ReadInt32(), y = arguments.ReadInt32();
            answer.Write(MyMod.WidgetAt(x, y) is not null);
        });
}
```

and ask it from a `[NetTest]`, which runs on the server:

```csharp
ClientLink.Request seen = ClientLink.Ask("MyMod.SeesWidget", w => { w.Write(x); w.Write(y); });

yield return Wait.Until(() => seen.Answered, "the client to say whether it sees the widget");

Assert.True(seen.Read().ReadBoolean());
```

If you need to wait for the information, do not poll with `Wait.Until` which will saturate the connection. Instead, use `AwaitAnswer`:

```csharp
yield return ClientLink.AwaitAnswer("MyMod.SeesWidget", w => { w.Write(x); w.Write(y); },
    reader => reader.ReadBoolean(), "the client to see the widget");
```

Asking more than `ClientLink.MaxQuestionsInFlight` questions without waiting for any of them fails the test that did it, by name, rather than wedging the run.

### Testing a client-only mod

Client-only mods still rely on the tier 3 server/client architecture. Anything you want to know about the client must still be implemented using the Query registration and answer system described above. The client can read pixels on the screen and report facts about them, but there not yet a way to send an image back to the server or the output report.

## Parameterised tests

A test with parameters can run once per case, each reported and filterable by name.

```csharp
[LoadedTest]
[Case(1)]
[Case(2)]
public void Small_numbers_are_positive(int value) => Assert.True(value > 0);
```
Cases can be created from a collection of objects:

```csharp
public static IEnumerable<string> Items => Subject.NamesOf<ModItem>();

[LoadedTest]
[CaseSource(nameof(Items))]
public void Every_item_has_a_display_name(string name)
{
    Assert.True(ModContent.TryFind(Subject.Name, name, out ModItem item));
    Assert.False(string.IsNullOrWhiteSpace(item.DisplayName.Value), $"item '{name}' has no display name");
}
```

That is where the feature earns its place. The cases only exist once the game has loaded, so they cannot be written out by hand, and discovery runs in the game for every tier above zero. Against ExampleMod the second example alone expands to 178 tests, one per registered item, each named and filterable.

## Realtime testing

A tier 2 test body is a coroutine, driven one step per game tick. It yields a `Wait` to say where it may be suspended:

```csharp
[GameTest(Band = Band.Cavern, Timeout = 600)]
public IEnumerator A_slime_falls(ITestContext ctx)
{
    NPC slime = ((TestContext)ctx).SpawnNPC(NPCID.BlueSlime, 8, 2);
    float start = slime.position.Y;

    yield return Wait.Until(() => slime.velocity.Y == 0, "the slime lands");

    Assert.True(slime.position.Y > start, "it should have fallen");
}
```

Vanilla locks the game to 60 ticks per second, which can slow down tests that take hundreds or thousands of ticks (e.g. a boss battle simulation). By hooking Thread.Sleep(), Testaria can accelerate this:

```
SPEED=max scripts/run-tests.sh   # as fast as the CPU manages
SPEED=300 scripts/run-tests.sh   # 300 ticks per second
```

or from the console, mid-run: `testaria speed max`, `testaria speed 300`, `testaria speed realtime`.

What fast forward cannot preserve is anything keyed to the wall clock rather than to ticks: real elapsed time, a background `Task`, a timer. A test whose subject is any of those should opt out with `[RealTime]`, and it then runs at 60 tps while the rest of the run does not.

```csharp
[GameTest(Band = Band.Cavern)]
[RealTime]
public IEnumerator A_cooldown_measured_in_real_seconds(ITestContext ctx) { ... }
```

`[RealTime]` works on a whole class as well as a single method. The run's default speed is restored as soon as the test(s) finishes.

## Pausing and single stepping

A test can stop the world and walk it forward one tick at a time:

```csharp
[GameTest(Band = Band.Cavern)]
public IEnumerator A_slime_lands_on_the_third_tick(ITestContext ctx)
{
    var box = (TestContext)ctx;
    NPC slime = box.SpawnNPC(NPCID.BlueSlime, 8, 2);

    ctx.Pause();                       // the world stops after this tick

    yield return ctx.Step(2);          // exactly two ticks pass
    Assert.True(slime.velocity.Y > 0, "it should still be falling");

    yield return ctx.Step();           // one more
    Assert.Equal(0f, slime.velocity.Y);

    ctx.Resume();
}
```

`[StartPaused]` freezes the world before the body's first yield.

From the console, the same controls work on whatever is running: `testaria pause`, `testaria step 5`, `testaria resume`. `testaria status` reports the speed and whether the world is frozen, so a paused run never looks like a hung one.

## Seeds and reproducibility

By default, each test seeds the RNG based on the test's identity, and reports that seed in the test output:

```xml
<testcase name="A_slime_falls" classname="MyModTests.SlimeTests" testaria-seed="991526881" />
```

You can specify the seed for a test, if you have a RNG-driven failure:

```csharp
[GameTest(Band = Band.Cavern)]
[Seed(4242)]
public IEnumerator A_slime_drops_its_banner(ITestContext ctx) { ... }
```

## Boxes and what they cost

A tier 2 or tier 3 test runs inside a box the arena leases it: a rectangle of world, in a band the test names, with a gutter of dead space around it. Teardown deactivates everything the test spawned and puts the ground back exactly as it was, so the next tenant of that slot inherits nothing.

Ask for more room when a test needs it, with `[GameTest(Width = 160, Height = 96)]`. A test that needs a whole world rather than a box says `[FreshWorld]`. FreshWorld is seconds slower than even the largest GameTest box size, so use it sparingly.

## Escapes

An entity of a test's own that leaves its box is recorded for review but does not fail the test. It appears in the report, as `<system-out>` on that test's `<testcase>`:

```xml
<testcase name="A_slime_falls" classname="MyModTests.SlimeTests">
  <system-out>NPC 3 left the box at tick 40</system-out>
</testcase>
```

That note is usually the explanation for a neighbouring box behaving oddly a few tests later, which is otherwise very hard to work out.

Something that enters a test that shouldn't is instead considered **contamination**, which errors the test.

## A box is a region, not a sandbox

A box isolates a rectangle of world. It has nothing to say about a static field, and neither the escape watch nor the ground restore can help you there: a test that sets `Main.afterPartyOfDoom` triggers a vanilla routine that kills every town NPC in the world, not the ones inside the box.

So state outside the box has to be put back, and putting it back on the last line stops happening the moment an assertion above it fails. Register it instead, and the runner does it at teardown however the test ended:

```csharp
[GameTest(Band = Band.Surface)]
public IEnumerator A_marked_npc_is_spared(ITestContext ctx)
{
    var box = (TestContext)ctx;

    // Read, register the undo, and write, in one step.
    box.Change(() => SomeMod.Sets.Vulnerable[NPCID.Guide], v => SomeMod.Sets.Vulnerable[NPCID.Guide] = v, false);

    // Or the long form, when the undo is not a simple assignment.
    box.Restore(() => SomeCache.Clear());

    ...
}
```

Restorations run in reverse order, so nesting unwinds the way a stack does, and one throwing does not strand the rest.

## Placing a tile the way a player does

`ctx.PlaceTile` calls `WorldGen.PlaceTile`, which puts the tile there and tells nobody. In particular it does **not** fire `ModTile.PlaceInWorld` or `GlobalTile.PlaceInWorld`: tModLoader calls those from `Player` alone, when somebody places a tile from an item.

A great deal of mod behaviour hangs off that hook. InnoVault creates its TileProcessor entities there, so the obvious test, place the tile and wait for the entity, waits out its timeout for something that never comes.

```csharp
box.PlaceTileAsPlayer(4, 4, ModContent.TileType<MyTile>());
```

places the tile and then announces it, returning false if the tile did not go down. It is the hook, not a simulated player: no item is consumed, nothing checks reach, and no animation plays.

## A suite that skips everything is not a suite that passed

That honesty has a failure mode of its own. A suite whose subject is another mod skips itself when that mod is absent, and tModLoader drops a mod that fails to load against a build it was not compiled for, which it does whenever the game updates under you. Every skip is then correct and the sum of them is a run that reports success having tested nothing.

So a run can be asked to prove it did something:

```
testaria run --mod MyMod --mod MyModTests --require 100
MIN_TESTS=100 scripts/run-tests.sh
```

Fewer tests than that actually running is a failure, with a message saying so. Worth setting for any suite whose subject is another mod, which is every suite this framework is for.

**The commonest cause of it is caught without being asked.** A mod that throws during its load pass is disabled by tModLoader, and the game carries on. `testaria` tells the run which mods it installed, and the run refuses to start if any of them is absent:

```
1 tests: 0 passed, 0 failed, 1 errored, 0 skipped
  ERROR Testaria.Preflight.The run was refused before it started:
      This run required mods Daybreak, DaybreakTests, which did not load. ...
      Check the log for the load error. Loaded: ModLoader, Testaria.
```

That is a report rather than silence, because the harness waits for one and a run that produced none would time out, which says much less than a named error. Without the check, the same scenario prints `0 tests: 0 passed` and exits 0.

A run that discovers **no** tests at all, or whose filter matches none of them, is an error for the same reason: reported on the console and then written to the report as an empty pass, it leaves the person watching and the harness reading with opposite verdicts.

**And a test that asserts nothing says so.** Passing without a single assertion reaching the body is not an error, since "this does not throw" is a real thing to test, but it is worth reading:

```xml
<testcase name="Every_processor_maps_to_its_mod" classname="MyModTests.RegistrationTests">
  <system-out>This test passed without making a single assertion, so it proved nothing.</system-out>
</testcase>
```

A test looping over a registry that is always empty does exactly that.

## Test Outcomes

| Outcome | Means |
|---|---|
| Failed | An assertion did not hold. The mod (or game) is broken. |
| Errored | The test threw an exception, could not run properly, or had its box contaminated. The test is broken. |
| Skipped | Intentionally omitted, or the runner could not honour what it asked for (e.g. FreshWorld). |
| Blocked | Didn't run because it couldn't. |

## Retained boxes, and tests that never ran

Every test gets its own region of the game world to run, a "box". With `KeepFailedBoxes`, on by default, a test that fails keeps its box. The tiles it placed, the entities it spawned and whatever state it left behind all stay exactly as they were, so you can load the world and go and look.

A run with many failures can run out of room. When that happens the tests that could not be given a box are reported as **blocked**:

```
1324 tests: 1290 passed, 9 failed, 0 errored, 25 blocked, 0 skipped
```

A blocked test's message names what is holding the space and what to do about it:

> This test never ran: the arena had no box for it. 49 of 49 slots are retained from earlier failures and are never reused, so that the state a failing test left behind survives for you to go and look at. Inspect them, then rerun. Set `KeepFailedBoxes` to false to give that ground up instead.

**A run containing blocked tests fails**, even when everything that actually ran passed. In the JUnit report a blocked test is written as an `<error type="Testaria.Blocked">` rather than as `<skipped>`.

## Run the self tests

```
scripts/run-all.sh
```

Five gates, fastest-failing first: the core self-tests, the green path (the self-test mod must pass in a live headless server), the red path (deliberate failures must be reported as failures), fresh worlds (tests asking for an untouched world get one), and calibration against ExampleMod.

Calibration needs `scripts/build-examplemod.sh` to have been run once, with `EXAMPLEMOD_SRC` pointing at the `ExampleMod` directory inside a [tModLoader](https://github.com/tModLoader/tModLoader) checkout.

### Environment variables

Nothing here knows where anything sits on your machine. Every path outside the repository is an environment variable, resolved in `scripts/paths.sh`, and the defaults cover only the conventional locations:

| Variable | What it is | Default |
| --- | --- | --- |
| `TML_PATH` | A tModLoader install, the directory holding `tModLoader.dll` and `tMLMod.targets` | Found by asking Steam: every library folder in `steamapps/libraryfolders.vdf` is checked, not just the default one. Set this for a GOG install or anything else Steam does not know about |
| `MODS_SRC` | Where a mod build leaves its `.tmod`, the `Mods` directory under tModLoader's save path | The save path of a **dev** build, since 1.4.5 is only available as one |
| `EXAMPLEMOD_SRC` | `ExampleMod` inside a tModLoader source checkout | None. It is a checkout you made, not something an install provides |
| `SPEED` | [Fast forward](#fast-forward) for the run: `max`, or a number of ticks per second | Unset, meaning the game's own 60 tps |
| `RUN_SEED` | Shifts every test's [seed](#seeds-and-reproducibility) at once, for rerunning a suite against different rolls | 0, so a run draws the same rolls everywhere |
| `MIN_TESTS` | Fails the run unless at least this many tests actually ran, rather than skipped | 0, meaning no such demand |
| `SEED` | The world's generation seed, which is a different thing entirely | 42 |

The build itself reads `TML_PATH` too, so an install anywhere unusual needs setting once and no more.

### Partial tests

```
dotnet test                                            # core only, no game needed
BLANK=1 scripts/run-tests.sh                           # the self-test mod in a live server
FILTER='Zombie|Skeleton' BLANK=1 scripts/run-tests.sh  # regex; just the matching tests
MODE=list BLANK=1 scripts/run-tests.sh                 # list tests without running them
scripts/check-red.sh                                   # prove failures are reported as failures
scripts/run-fresh.sh                                   # a dedicated server per [FreshWorld] test, very slow
```

`run-tests.sh` provisions a scratch save directory, drops the `.tmod` files in, launches a headless server on its own virtual display, pipes a console command, and maps the JUnit report to an exit code. It never touches a real installation's mods, worlds or players.

Still to come: parallel box execution.

## Building against tModLoader

`Testaria.Core` and its tests need nothing but the .NET SDK. The `Testaria` mod project needs a tModLoader install on the **1.4.5 line** (`net10.0`, C# 14).

The build looks for one in the platform's default Steam library; an install on another drive, in a second library folder, or from GOG needs `TML_PATH` set to the directory holding `tMLMod.targets`. Without an install, the mod project skips building the `.tmod` and says so, rather than failing, so a checkout with no game still builds and the core's tests still run.

To get 1.4.5 on Steam: tModLoader, gear icon, Properties, Betas, enter the password `iamacontributor` to unlock the branch, then select **`1.4.5-dev`**. Note that the `preview-*` branches are **not** 1.4.5, they are the monthly CI channel on the 1.4.4 line and install `net8.0` with `LangVersion 12.0`. See [tModLoader issue #5070](https://github.com/tModLoader/tModLoader/issues/5070).

## Repository layout

| Directory | What it is |
| --- | --- |
| [`src/Testaria.Core/`](src/Testaria.Core) | Game-independent core. No tModLoader reference, by design. |
| [`src/Testaria/`](src/Testaria) | The tModLoader-facing half. Needs a 1.4.5 install to build. |
| [`src/Testaria.Analyzers/`](src/Testaria.Analyzers) | The tier 0 boundary analyzer, shipped inside the `Testaria.Core` package. |
| [`src/Testaria.Tool/`](src/Testaria.Tool) | The `testaria` command: provision, run, report, exit code. Needs no game to build. |
| [`src/Testaria.Unit/`](src/Testaria.Unit) | Build-only package wiring a test project against an install, for tier 0 tests that need the game's types. |
| [`src/Testaria.Sdk/`](src/Testaria.Sdk) | Build-only package running a suite from MSBuild, carrying the CLI inside it. |
| [`tests/Testaria.Core.Tests/`](tests/Testaria.Core.Tests) | Self-tests for the core. Plain `dotnet test`, no game required. |
| [`tests/Testaria.Analyzers.Tests/`](tests/Testaria.Analyzers.Tests) | Self-tests for the analyzer, run against a stub of the Terraria surface. |
| [`tests/Testaria.Tool.Tests/`](tests/Testaria.Tool.Tests) | Self-tests for the CLI: its command line, its provisioning, and its reading of a report. |
| [`tests/TestariaSelfTest/`](tests/TestariaSelfTest) | The in-game self-test mod, which is the green path. |
| [`tests/TestariaRedTest/`](tests/TestariaRedTest) | Deliberately broken tests, which is the red path. |
| [`tests/TestariaExampleTest/`](tests/TestariaExampleTest) | The calibration suite, aimed at ExampleMod. |
| [`tests/TestariaLoadTest/`](tests/TestariaLoadTest) | The arena under load: 300 boxes, every size class, spanning columns, and a full entity pool. |
| [`templates/`](templates) | The two `dotnet new` templates. |
| [`scripts/`](scripts) | The headless harness and its gates, including `check-packages.sh`, which consumes the packages the way a stranger would. |
| [`build/`](build) | `Testaria.props`, for suites that live outside this repository. |

## What is built

This functionality is in `Testaria.Core`, doesn't reference tModLoader, and is all self-tested:

| Piece | What it does |
| --- | --- |
| `Assert`, `AssertionException` | xUnit-shaped assertion vocabulary, including xUnit's element-by-element equality for collections. Separate from xUnit because no stock runner can host inside a mod's `AssemblyLoadContext` |
| `TestResult`, `TestRunResult`, `JUnitXmlWriter` | Results and JUnit XML reporting, the one format every CI system reads without a custom reporter |
| `TileRect`, `Band`, `WorldGeometry` | Tile geometry and the depth band model, including band spans for tests that cross a boundary |
| `Arena`, `BoxLease`, `BoxRequest`, `ArenaOptions` | Leasing, recycling, and quarantine of test boxes, banded and spanning |
| `TestTier`, attributes, `TestDiscovery` | The tier model and reflection-based discovery, with malformed tests reported rather than dropped |
| `Wait`, `TestCoroutine` | The tick scheduler: coroutine test bodies driven one step per tick, with tick budgets and nested enumerators |
| `TestRunner`, `TestSession` | Drives discovery, the arena and the scheduler from the game's update loop |
| `BlankWorldLayout` | A deterministic stone-and-air world, with reserved ground for whatever vanilla insists exists |
| `PortableFileName`, `ResultsLocation` | Report paths valid on every OS Terraria runs on |
| `GameState`, `[RequiresLoadedGame]` | The runtime half of the tier 0 boundary: a flag the game raises, and a guard that fails loudly without it |
| `RunPacing`, `TickRateGovernor` | How fast a run may simulate, and whether it simulates at all: realtime, a bounded rate, or as fast as the machine manages |
| `TestSeed`, `IRandomControl` | Which seed a test's randomness starts from, derived from the test's identity so filtering a suite cannot change it |
| `BiomeScan` | How far the game looks around a player to decide their biome, 169 by 124 tiles, which is the spacing biome isolation would really need |
| `ISteppableContext` | Stopping the world and stepping it a tick at a time |

## License

MIT. See [`LICENSE`](LICENSE).
