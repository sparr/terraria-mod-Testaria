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
| `--config <path>` | Seed a mod config, named as tModLoader names it: `<ModName>_<ConfigClassName>.json`. Repeatable. Without it a suite can only test a mod's defaults |
| `--keep-scratch` | Keep the save directory, and say where it is |
| `--verbose` | Print the server's own log as it happens, for a run that will not start |
| `--help` | List all of the command line flags |

`testaria list` takes the same options and catalogs the tests without running any of them.

### From a build, rather than by hand

`Testaria.Sdk` can perform tests as part of your `dotnet build`:

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

## Every property, against its own output

```csharp
[LoadedTest]
public void Nothing_chokes_on_what_it_just_produced()
    => Assert.SettersAcceptTheirOwnGetters(new BossTestData());
```

For each public read-write property, read it and write the same value straight
back. The question is only whether that throws. Values are free to be
normalized, clamped or reformatted on the way through; none of that is a
failure here.

Give it a **newly constructed** object. The bug this exists for lives in the
empty, null and zero cases, which is where a getter hands its own setter
something the setter was never written to parse.

It is worth having because it needs no knowledge of the type. Found in a mod
nobody here wrote, by exactly the line above:

```
Assert.SettersAcceptTheirOwnGetters() Failure
On a BossTestData, 1 property could not take its own value:
  diedString: reading it gave "", and writing that back threw FormatException: The input string '' was not in a correct format.
```

That property renders an unset result as the empty string and parses with
`Single.Parse`, which throws on one. Its sibling `timeString` does the same job
with `int.TryParse` and is safe. Reading a value into a text field and writing
it back unedited is what a user interface does constantly, and the two
properties disagree about whether that works.

Every property is tried before anything is reported, so one bad property does
not hide the next. Read-only properties, private setters and indexers are
skipped.

### And that it stops changing

```csharp
[LoadedTest]
public void Nothing_drifts_when_written_twice()
    => Assert.SettersSettleAfterOneWrite(new BossTestData());
```

Read, write it back, read, write that back, read again: the last two reads must
agree. The **first** write may change the value, because normalizing what it
was given is a property doing its job, and `"2:5"` coming back as `"2:05"` is
correct. The second must not, because a property that keeps moving has no
resting state, and every pass through the interface it belongs to drifts a
little further.

This is the sibling of the check above and finds a different fault. That one is
about a crash; this one is about a value that never settles, which appends,
re-escapes or truncates a little more each time and throws nothing while doing
it:

```
Assert.SettersSettleAfterOneWrite() Failure
On a EscapesEveryTime, 1 property does not settle:
  Text: started as "a&b", became "a&amp;b" after one write, and "a&amp;amp;b" after a second, so it never settles
```

That one is from this framework's own test for it, not from a real mod: unlike
the check above, this has not yet caught anything in the wild. The message is
asserted verbatim by a test, so the example cannot drift from the code.

A property that cannot be written at all is reported rather than passed over:
it cannot be shown to settle, and passing would claim it had been checked.

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

If a later test fails, it could be the case that this escapee wandered/fell/etc into that test's box. In that case, the latter test will error, and you can use this result to figure out why.

## A box is a region, not a sandbox

A box isolates a rectangle of the world. It does not isolate anything world-global. e.g. A test that sets `Main.afterPartyOfDoom` kills every town NPC in the world on the next update, not the ones standing in its box, and clears half a dozen saved-spawn flags along with them.

So global state a test changes has to be put back, and putting it back on the last line of the test stops happening the moment an assertion above it fails. Register the undo instead, and the runner runs it at teardown however the test ended:

```csharp
[GameTest(Band = Band.Surface)]
public IEnumerator A_test_that_changes_the_time_puts_it_back(ITestContext ctx)
{
    var box = (TestContext)ctx;

    // Change takes three things: how to read the value, how to write it, and
    // what to set it to. It reads Main.dayTime, registers putting that value
    // back at teardown, and only then writes the new one.
    box.Change(() => Main.dayTime, value => Main.dayTime = value, false);

    // Restore is the long form, for an undo that is not one assignment. This
    // one empties a list that a hook in the test mod appends to.
    box.Restore(PlacementWatcher.Placements.Clear);

    ...
}
```

Restorations run in reverse order, popping from a stack, and one exception won't stop the rest.

## Placing a tile the way a player does

`ctx.PlaceTile` calls `WorldGen.PlaceTile`, which puts the tile there but doesn't announce it, so `ModTile.PlaceInWorld` and `GlobalTile.PlaceInWorld` won't fire. You probably want `PlaceTileAsPlayer`:

```csharp
box.PlaceTileAsPlayer(4, 4, ModContent.TileType<MyTile>());
```

## Test Outcomes

| Outcome | Means |
|---|---|
| Failed | An assertion did not hold. The mod (or game) is broken. |
| Errored | The test threw an exception, could not run properly, or had its box contaminated. The test is broken. |
| Skipped | Intentionally omitted, or the runner could not honor what it asked for (e.g. FreshWorld). |
| Blocked | Didn't run because it couldn't. |

## Retained boxes, and tests that never ran

Every test gets its own region of the game world to run, a "box". With `KeepFailedBoxes`, on by default, a test that fails keeps its box. The tiles it placed, the entities it spawned and whatever state it left behind all stay exactly as they were, so you can load the world and go and look.

A run with many failures can run out of room. When that happens the tests that could not be given a box are reported as **blocked**. A blocked test's message names what is holding the space and what to do about it:

> This test never ran: the arena had no box for it. 49 of 49 slots are retained from earlier failures and are never reused, so that the state a failing test left behind survives for you to go and look at. Inspect them, then rerun. Set `KeepFailedBoxes` to false to give that ground up instead.

## Run the self tests

```
scripts/run-all.sh
```

Eight gates, fastest-failing first: the core self-tests, the green path (the self-test mod must pass in a live headless server), the red path (deliberate failures must be reported as failures), the packages consumed the way a stranger would, the templates generated and then built and run, tier 3 both with a client and without one, fresh worlds (tests asking for an untouched world get one), and calibration against ExampleMod. A ninth, the arena under load, is opt-in with `RUN_LOAD=1`, since it takes longer than the other steps combined.

Calibration needs `scripts/build-examplemod.sh` to have been run once, with `EXAMPLEMOD_SRC` pointing at the `ExampleMod` directory inside a [tModLoader](https://github.com/tModLoader/tModLoader) checkout.

### Environment variables

Nothing here knows where anything sits on your machine. Every path outside the repository is an environment variable, resolved in `scripts/paths.sh`, and the defaults cover only the conventional locations:

| Variable | What it is | Default |
| --- | --- | --- |
| `TML_PATH` | A tModLoader install, the directory holding `tModLoader.dll` and `tMLMod.targets` | Found by asking Steam: every library folder in `steamapps/libraryfolders.vdf` is checked, not just the default one. Set this for a GOG install or anything else Steam does not know about |
| `MODS_SRC` | Where a mod build leaves its `.tmod`, the `Mods` directory under tModLoader's save path | The save path of a **dev** build, since 1.4.5 is only available as one |
| `EXAMPLEMOD_SRC` | `ExampleMod` inside a tModLoader source checkout | None. It is a checkout you made, not something an install provides |
| `SPEED` | [Fast forward](#realtime-testing) for the run: `max`, or a number of ticks per second | Unset, meaning the game's own 60 tps |
| `RUN_SEED` | Shifts every test's [seed](#seeds-and-reproducibility) at once, for rerunning a suite against different rolls | 0, so a run draws the same rolls everywhere |
| `MIN_TESTS` | Fails the run unless at least this many tests actually ran, rather than skipped | 0, meaning no such demand |
| `BEHAVIOUR` | `1` asks for the sweep's [behaviour checks](#invariant-tests), which lease a box per piece of content across every enabled mod | 0, so they skip carrying that reason |
| `ECONOMY` | `1` asks for the sweep's [recipe checks](#invariant-tests), whose failure is a balance claim rather than a defect | 0, so they skip carrying that reason |
| `SEED` | The world's generation seed, which is a different thing entirely | 42 |
| `MEM_MAX` | How much memory the game is allowed, when it can be capped at all | `4G` |
| `MEM_CAP` | `0` runs the game without a cap. The cap needs a systemd **user manager**, not just the `systemd-run` binary, so the gates probe for one and go without when there is none, which is the common shape of a CI runner. Set it to 0 to exercise that path on a machine that could cap | 1 |
| `STRICT` | `1` makes a gate that will not run a **failed** gate, rather than a line of output nobody reads | 0 |
| `UNCOVERED` | Gate names, one per line, that this run declares up front it does not cover. They are reported as `declared-uncovered` and do not fail the run; anything else that tries to skip still does under `STRICT`. A hosted CI runner uses it for tier 3, which needs game assets it cannot have | empty |

### Writing the answers down once

```
scripts/discover-paths.sh
```

Discovery is per process, so an install Steam keeps somewhere unusual is found again on every run and forgotten again in between. This works the paths out once and writes them into two files git ignores, with a committed `.example` beside each:

| File | Read by | Why it is separate |
| --- | --- | --- |
| `scripts/paths.local.sh` | the gates, through `scripts/paths.sh` | shell, sourced before anything is computed |
| `Directory.Build.local.props` | `dotnet build`, and any editor or IDE | MSBuild does not run bash |

### Partial tests

```
dotnet test                                            # core only, no game needed
BLANK=1 scripts/run-tests.sh                           # the self-test mod in a live server
FILTER='Zombie|Skeleton' BLANK=1 scripts/run-tests.sh  # regex; just the matching tests
MODE=list BLANK=1 scripts/run-tests.sh                 # list tests without running them
scripts/check-red.sh                                   # prove failures are reported as failures
scripts/run-fresh.sh                                   # a dedicated server per [FreshWorld] test, very slow
```

## Continuous integration, on your own machine

```
scripts/ci-local.sh
```

The game tiers need a tModLoader install, and a hosted CI runner has none. Obtaining one there is real work: download the public dedicated-server zip, decompile and build tModLoader with an ownership key held as a repository secret, and cache the result. That job is planned, and everything it does is a reconstruction of what a machine with the game already has. So the gates can be run under CI discipline here first.

`ci-local.sh` wraps `run-all.sh` with the parts that make a run worth keeping:

| It does | Because |
| --- | --- |
| Preflights `dotnet`, `python3`, `rsync`, `Xvfb`, `systemd-run`, `flock`, the install, and the ExampleMod sources | A missing `Xvfb` otherwise surfaces as a tier 3 client that joins and then goes quiet |
| Builds ExampleMod first | Otherwise the calibration gate calibrates against whatever `.tmod` was last built, which is not a known version of anything |
| Runs the gates with `STRICT=1` | A gate that cannot run is a failed gate. `SKIP_*` exists for a developer's quick loop; a CI run that honored it would report success for a suite it never ran |
| Keeps a directory per run | Every gate's log, every JUnit report, `gates.tsv`, and a summary, all still there after the terminal is gone |
| Takes a lock, and closes it before starting anything | Two runs share one save directory and one port, so the second fails in ways that look like the framework's fault. The lock is a file descriptor and children inherit those, so without `9>&-` the `VBCSCompiler` that `dotnet build` leaves running holds it for ten minutes after the run ends |
| Appends to `history.tsv` | Which commit got which verdict, which is what `--if-new` reads |

| Option | What it does |
| --- | --- |
| `--if-new` | Run only if `HEAD` is a commit no run has covered, and the tree is clean. Does nothing otherwise, so a timer can fire as often as it likes |
| `--force` | Run even for a commit already recorded |
| `--load` | Include the arena load gate, which is opt-in because it measures rather than asserts |
| `--keep N` | Keep the N most recent run directories, default 20 |
| `--runs-dir DIR` | Where runs are kept. Defaults to `ci-runs/`, or `TESTARIA_CI_RUNS` |

A run leaves `ci-runs/latest` pointing at the most recent one:

```
ci-runs/latest/summary.txt       gates, verdicts, and how much each one proved
ci-runs/latest/run.meta          commit, branch, host, install, load average
ci-runs/latest/gates.tsv         gate, verdict, seconds
ci-runs/latest/run.log           everything the run printed
ci-runs/latest/<gate>.log        one per gate
ci-runs/latest/<gate>.xml        the JUnit report from every gate that writes one
```

### Running it on a timer

`scripts/systemd/` has a user service and timer. The service reads the one machine-specific value from a file outside the repository, for the same reason [`scripts/paths.local.sh`](#writing-the-answers-down-once) is untracked:

```
echo "TESTARIA_CHECKOUT=$PWD" > ~/.config/testaria-ci.env
cp scripts/systemd/testaria-ci.* ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now testaria-ci.timer
```

It checks every fifteen minutes and runs only when there is a commit no run has covered. `systemctl --user list-timers testaria-ci.timer` says when it will next fire, `journalctl --user -u testaria-ci.service` says what happened, and `ci-runs/latest/summary.txt` says what it found.

Two things it deliberately does not do. It does not run against a dirty tree: a scheduled run records a verdict against a commit, and a tree with edits in it is not any commit. And it does not isolate itself from the game's save directory, because building a mod means invoking tModLoader, which writes the `.tmod` into the save path's `Mods` folder and offers no way to redirect it. Playing tModLoader while a run is going will see the mods it builds.

Linux only, for `Xvfb`, `systemd-run`, and `flock`. The core tiers already run on three operating systems in GitHub Actions, which is where the portability that matters is asserted.

## Building against tModLoader

`Testaria.Core` and its tests need nothing but the .NET SDK. The `Testaria` mod project needs a tModLoader install on the **1.4.5 line** (`net10.0`, C# 14).

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
| [`tests/TestariaSweepTest/`](tests/TestariaSweepTest) | The sweeping checks: asked of every type and every piece of content in whichever mods a run enables, naming none of them. |
| [`tools/`](tools) | Odds and ends that are neither framework nor gate, such as a `.tmod` reader. |
| [`tests/TestariaLoadTest/`](tests/TestariaLoadTest) | The arena under load: 300 boxes, every size class, spanning columns, and a full entity pool. |
| [`templates/`](templates) | The two `dotnet new` templates. |
| [`scripts/`](scripts) | The headless harness and its gates, including `check-packages.sh`, which consumes the packages the way a stranger would, and `ci-local.sh`, which runs all of them the way CI would. |
| [`scripts/systemd/`](scripts/systemd) | A user service and timer, for running the game tiers on a schedule. |
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
