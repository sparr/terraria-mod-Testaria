# Testaria

Unit, integration, and gameplay testing for Terraria mods built on tModLoader.

**Status: early alpha.** All four tiers run end to end in a live headless game, driven by the `testaria` command line tool, and the framework has been calibrated against ExampleMod. Tier 3 covers netcode, with a real client process joining a real server, and reaches far enough into a client to read back pixels it drew; what is out of scope is rendering *tooling* and input. Nothing is published to nuget.org or the Workshop yet. The design lives in [`PLAN.md`](PLAN.md).

## What does it do?

Some Terraria mods implement their own unit testing. A few load their mod into the game server to confirm it doesn't immediately crash. Almost none perform any significant integration testing. Testaria aims to fill a gap in the Terraria modding ecosystem by making it easy for any mod to implement one or more tiers of testing ranging from pure logic unit tests to "actually launch the game and perform some actions in a multiplayer world and measure the outcome over thousands of ticks".

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
| `[RealTime]` | Exempts a test from the run's fast forward, running it at 60 ticks per second |
| `[StartPaused]` | Freezes the world as the test begins, for a test that steps it by hand |
| `[Seed]` | Pins a test's randomness to a particular seed, rather than the one derived from its name |
| `[RequiresLoadedGame]` | Declares that a member needs the game, so tier 0 code cannot reach it by accident |

## The tier 0 boundary

The most dangerous thing a test framework for a game can do is go green while proving nothing, and Terraria offers an easy way to do exactly that. Loader state does not throw outside the game. It answers. Measured against tModLoader 1.4.5 with no game started:

| Touched | Result outside the game |
| --- | --- |
| `ItemID.CopperShortsword`, `ItemID.Count` | Works. Consts are inlined at compile time |
| `new Item()` | Works, constructs fine |
| `Main.maxTilesX` | **Throws** `TypeInitializationException` |
| `ContentSamples.ItemsByType.Count` | **Returns 0** |
| `ModLoader.Mods.Length` | **Returns 0** |
| `new Item().Name` | **Returns `""`** |
| `Lang.GetItemNameValue(3507)` | **Returns `""`** |
| `ItemID.Sets.Deprecated.Length` | **Returns 6196**, the vanilla count, never resized for mods |

`Main` is the safe one: it fails loudly, so the mistake corrects itself. Everything else answers anyway, so `Assert.Empty(ContentSamples.ItemsByType)` passes in a unit test host and means nothing at all.

Three things keep tier 0 honest, in order of how much they ask of you.

**The core carries no tModLoader reference.** `Testaria.Core` references nothing from the game, so a test project that references only the core cannot reach any of the surface above. This is the whole defence for most projects, and the templates are built this way.

**An analyzer, for projects where a reference is unavoidable.** Referencing `Testaria.Core` also installs the tier 0 boundary analyzer. In an assembly with no load pass underneath it, reaching for the loader-dependent surface is an error rather than a silent lie:

```
error TSTA001: 'ContentSamples.ItemsByType' only means anything after a load pass,
and this assembly runs without one
```

| Rule | What it catches |
| --- | --- |
| `TSTA001` | `ContentSamples`, `ModContent`, `Lang`, `Language`, `ModLoader.Mods` and friends, and any `*ID.Sets` member, used where no game has loaded |
| `TSTA002` | A call to a `[RequiresLoadedGame]` member from a caller that has not declared the same |

The analyzer stands down on its own in an assembly that declares a type implementing `ILoadable`, since a mod assembly cannot run without a load pass anyway. To turn it off deliberately, set `<TestariaLoaderStateAnalysis>false</TestariaLoaderStateAnalysis>`.

**`[RequiresLoadedGame]`, for code that really does need the game.** Marking a member says so out loud. The analyzer then stops blaming that member and starts blaming undeclared callers, so the dependency travels up the call graph to the test that has to answer for it:

```csharp
[RequiresLoadedGame("reads the sample cache")]
static int ModdedItemCount() => ContentSamples.ItemsByType.Count - ItemID.Count;
```

For anything the analyzer cannot see, such as reflection or a cross-mod `Mod.Call`, `GameState` is the runtime half:

```csharp
GameState.Require("ContentSamples");   // throws LoaderStateException if no game has loaded
```

`GameState.IsLoaded` is false in every `dotnet test` host and true from the end of the Testaria mod's load pass until it unloads. `LoaderStateException` is its own type so a suite can tell "this test is in the wrong tier" apart from "the code under test is broken".

## Getting started on your own mod

Set up the templates:

```
dotnet new install Testaria.Templates
dotnet new testaria-unit-tests -n MyUnitTests                  # tier 0, out-of-game tests
dotnet new testaria-mod-tests  -n MyModTests --subject MyMod   # tiers 1 and 2, in-game tests
```

Tier 0 is an ordinary xUnit project, so it runs wherever `dotnet` does:

```
cd MyUnitTests && dotnet test
```

Tiers 1 and 2 need the game. Building writes `MyModTests.tmod` straight into your tModLoader `Mods` folder:

```
cd MyModTests && dotnet build
```

Then launch tModLoader, enable **Testaria**, your own mod, and **MyModTests**, load any world, and run the command in chat:

```
/testaria run MyModTests
```

It replies with the counts, and writes a JUnit report to `<tModLoader save path>/Testaria/MyModTests.xml`.

The same command works on a server console without the leading slash, which is what the command line tool sends for you.

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
| `--speed max` | [Fast forward](#fast-forward) the whole run |
| `--seed <n>` | Shift every test's [seed](#seeds-and-reproducibility) |
| `--results <path>` | Copy the JUnit XML report somewhere CI will look |
| `--require <n>` | Fail unless at least n tests actually ran, which catches a suite that skipped everything |
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

Every question is a handle you wait on, never a value you read: the answer is a packet, and it arrives some ticks later. `ClientLink.Ping()` is the smallest round trip there is, `ClientLink.AskTile(x, y)` asks what the client believes about a tile, and `ClientLink.AwaitSection(x, y)` gives it the ground to have a belief about and waits until it really has it.

That last one matters more than it sounds. Sending a section is not instant, and a section that has not arrived looks exactly like empty ground: the client answers "no tile" for everything in it. A test that waits a fixed number of ticks and then reads an empty space is reading its own impatience. `AwaitSection` waits until both sides agree about the tile, which is the cheapest honest proof that the ground is there.

**Without a client, a `[NetTest]` is skipped, never run.** A netcode test that quietly runs single-player passes while proving nothing, so the session counts connected clients rather than trusting a flag that says one was launched. The report says so plainly: `Needs tier MultiProcess but this environment supports up to World.`

What the harness does for you, each piece of it required to get a client into the world:

- **Seeds the client's configuration.** A fresh save directory stops at "Select language", then at a welcome dialog, then at change notes, each waiting for a click. None can be suppressed by a mod, because they stand in front of mod loading.
- **Provides a framebuffer.** A server is happy with `SDL_VIDEODRIVER=dummy`; a client started that way exits within five seconds. On Linux the harness starts `Xvfb` on a spare display and stops it afterwards.
- **Drives the join from inside the game.** Nothing on the command line reaches `Main.AutoJoin`, so the client half of Testaria fabricates a character and connects, and does so only when `-testariajoin` is present, so a person's own game is untouched.

Rendering, pixel diffing, and input replay remain out of scope: tier 3 here means netcode.

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

`ctx.Step(n)` grants the world exactly `n` ticks and returns a wait covering them, so the body resumes on the last one and can ask for more. `[StartPaused]` freezes the world before the body's first yield, for a test that wants to set up and inspect before anything moves.

This is the same gate Terraria's own debug stepper uses: `DoUpdate` keeps running and only `DoUpdateInWorld` is skipped, so a frozen tick is a shape the game already produces. A test that pauses and then neither steps nor resumes is thawed by the harness after 1800 frames, with a note on its result, rather than hanging the run.

From the console, the same controls work on whatever is running: `testaria pause`, `testaria step 5`, `testaria resume`. `testaria status` reports the speed and whether the world is frozen, so a paused run never looks like a hung one.

## Boxes and what they cost

A tier 2 or tier 3 test runs inside a box the arena leases it: a rectangle of world, in a band the test names, with a gutter of dead space around it. Teardown deactivates everything the test spawned and puts the ground back exactly as it was, so the next tenant of that slot inherits nothing.

The numbers behind that are measured rather than chosen, over a corpus of 967 tests:

| Setting | Value | Why |
| --- | --- | --- |
| Default box | 48 by 32 tiles | The furthest any test's own entities ranged was 25 by 21, and the largest patch of ground any of them changed was 5 by 3 |
| Quarantine | 12 ticks | 31 boxes of 33 were quiet the tick after teardown; the slowest took 2 |
| Gutter | 8 tiles | Covers tile framing and liquid. It does **not** cover biome scanning, which reaches 84 tiles, and cannot until boxes run concurrently |
| Snapshot and restore | 0.35 microseconds per tile | An ordinary box costs well under a millisecond to save and put back |

Ask for more room when a test needs it, with `[GameTest(Width = 160, Height = 96)]`. A test that needs a whole world rather than a box says `[FreshWorld]`, and that is a statement about semantics rather than cost: a column spanning an entire small world costs under two seconds to scrub, against five and a half to generate a blank world, so there is no width at which a fresh world is the cheaper option.

To collect the same measurements from your own suite:

```
testaria run --mod MyModTests --measure --results out.xml
```

which writes `out-arena.tsv` beside the report: a row per boxed test with the size it was granted, the ground it changed, how far its entities roamed, what snapshot and restore cost, and how many ticks the box took to go quiet.

## Escapes

An entity of a test's own that leaves its box is recorded for review, and never fails anything. Leaving may be exactly what the test is watching, and dragging it back would change the behaviour under test. It appears in the report, as `<system-out>` on that test's `<testcase>`:

```xml
<testcase name="A_slime_falls" classname="MyModTests.SlimeTests">
  <system-out>NPC 3 left the box at tick 40</system-out>
</testcase>
```

That note is usually the explanation for a neighbouring box behaving oddly a few tests later, which is otherwise very hard to work out.

Something that enters a test that shouldn't is instead considered **contamination**, which errors the test.

## A suite that skips everything is not a suite that passed

That honesty has a failure mode of its own. A suite whose subject is another mod skips itself when that mod is absent, and tModLoader drops a mod that fails to load against a build it was not compiled for, which it does whenever the game updates under you. Every skip is then correct and the sum of them is a run that reports success having tested nothing.

So a run can be asked to prove it did something:

```
testaria run --mod MyMod --mod MyModTests --require 100
MIN_TESTS=100 scripts/run-tests.sh
```

Fewer tests than that actually running is a failure, with a message saying so. Worth setting for any suite whose subject is another mod, which is every suite this framework is for.

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
| `TML_PATH` | A tModLoader install, the directory holding `tModLoader.dll` and `tMLMod.targets` | The `tModLoader` directory in the platform's default Steam library |
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
| [`templates/`](templates) | The two `dotnet new` templates. |
| [`scripts/`](scripts) | The headless harness and its gates, including `check-packages.sh`, which consumes the packages the way a stranger would. |
| [`build/`](build) | `Testaria.props`, for suites that live outside this repository. |

## What is built

This functionality is in `Testaria.Core`, doesn't reference tModLoader, and is all self-tested:

| Piece | What it does |
| --- | --- |
| `Assert`, `AssertionException` | xUnit-shaped assertion vocabulary. Separate from xUnit because no stock runner can host inside a mod's `AssemblyLoadContext` |
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
