# Testaria

Unit, integration, and gameplay testing for Terraria mods built on tModLoader.

> **Status: early alpha.** Tiers 0 through 2 run end to end in a live headless game, and the framework has been calibrated against ExampleMod. Tier 3 (multi-process) is not started. The design lives in [`PLAN.md`](PLAN.md).

## What this is

tModLoader has no test-authoring API. The closest thing in the tModLoader repository is a set of hand-built failure-case mods that a human runs by hand. Testaria aims to fill that gap with a framework that spans four tiers:

| Tier | Name | Runs in | Can test |
| --- | --- | --- | --- |
| 0 | Unit | a normal `dotnet test` host | Pure logic with no dependency on loader state |
| 1 | Loaded | tModLoader `-server`, no world | Content registration, recipes, ID sets, config, localization |
| 2 | World | tModLoader `-server`, world loaded | NPC AI over ticks, world gen, tile framing, drop tables |
| 3 | Multi-process | server plus client(s) | Netcode and sync, UI, rendering, input |

Tests are marked with attributes, and discovery finds them by reflection.

| Attribute | What it marks |
| --- | --- |
| `[LoadedTest]` | A tier 1 test: needs a completed load pass, but no world |
| `[GameTest]` | A tier 2 test: needs a world and a tick loop, and is given a box of its own |
| `[FreshWorld]` | A test no box can isolate, which needs a freshly generated world instead |
| `[Case]` | One set of arguments for a parameterised test, reported as a case of its own |
| `[CaseSource]` | A member supplying a parameterised test's cases, read during discovery |

The single most important rule the framework enforces is the **Tier 0 boundary**: the moment a test touches `Main`, `ModContent`, or `ContentSamples`, it depends on state that only a completed load pass establishes. In a bare test host those statics are default-initialized rather than absent, so such a test will often pass silently against garbage. `Testaria.Core` therefore carries no reference to tModLoader at all, which makes that boundary structural rather than advisory.

## Repository layout

```
src/
  Testaria.Core/        Game-independent core. No tModLoader reference, by design.
  Testaria/             The tModLoader-facing half. Needs a 1.4.5 install to build.
tests/
  Testaria.Core.Tests/  Self-tests for the core. Plain `dotnet test`, no game required.
```

More projects arrive as the tiers land. See `PLAN.md` section 4.2 for the full artifact matrix.

## What is built

All of it in `Testaria.Core`, all of it free of any tModLoader reference, and all of it self-tested.

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

## Getting started on your own mod

Two templates, because tier 0 and the tiers above it run in different places:

```
dotnet new install Testaria.Templates
dotnet new testaria-mod-tests  -n MyModTests --subject MyMod   # tiers 1 and 2, in-game
dotnet new testaria-unit-tests -n MyUnitTests                  # tier 0, no game
```

Both build as generated. The in-game one finds your tModLoader install itself, which is the fiddly part to get right by hand and most of why the templates exist; set `TML_PATH` if it guesses wrong. Their placeholder tests are meant to go green once and then be replaced: a scaffold that passes end to end proves your install path, scratch directory and world provisioning all work before you have written a line.

The attributes, `Assert`, `Wait`, `ITestContext` and `Band` live in `Testaria.Core`, an ordinary NuGet package, so a test mod compiles against that alone. A `.tmod` cannot ship NuGet output, but it does not need to: the package is a compile-time reference, and at run time the same assembly is already present because the Testaria mod carries it and `modReferences` names it.

`TestContext`, which places tiles and spawns entities, lives in the mod assembly rather than the package, because it touches Terraria types. The generated project shows how to reference it.

## Parameterised tests

A test with parameters and a source of values runs once per case, each reported and filterable by name. There is no separate `[Theory]` marker: the tier attribute already marks a method as a test, so having data parameters is what makes it parameterised. A leading `ITestContext` is the context, not data.

```csharp
[LoadedTest]
[Case(1)]
[Case(2)]
public void Small_numbers_are_positive(int value) => Assert.True(value > 0);

public static IEnumerable<string> Items => Subject.NamesOf<ModItem>();

[LoadedTest]
[CaseSource(nameof(Items))]
public void Every_item_has_a_display_name(string name) { ... }
```

`[CaseSource]` is the reason this exists. Cases that only appear once the game has loaded, every item a mod registers, every recipe it adds, cannot be written out by hand, and discovery runs in the game for every tier above zero. The ExampleMod suite goes from 39 tests to **924** that way.

They are `Case` and `CaseSource` rather than xUnit's `InlineData` and `MemberData` because tier 0 projects use xUnit and `Testaria.Core` together by design, and same-named types in both would make `using Xunit; using Testaria;` ambiguous.

## Waiting, and what it costs

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

A tick is not free. The dedicated server paces itself to real time at 60 Hz, so a tick is **16.7 ms of wall clock** and a thousand-tick test takes **16.7 seconds** no matter how fast the machine is. That makes the choice between `Wait.Until` and `Wait.Seconds` a performance decision rather than a matter of taste:

- `Wait.Until(predicate, "what you are waiting for")` finishes on the first tick the thing has happened. If it happens at tick 10, it costs ten ticks.
- `Wait.Seconds(3)` costs 180 ticks, three seconds, every run, whether the thing happened at tick 10 or not at all.

A suite of a hundred tests that each sleep a gratuitous extra second is an extra minute and a half on every run. Prefer the predicate, and reach for a fixed delay only when the elapsed time is itself the thing under test.

The description is worth supplying. It is what a timeout message names, and

```
Test exceeded its budget of 600 ticks while blocked on Wait.Until(the slime lands).
```

is a diagnosis, where `Wait.Until(...)` is a shrug.

`Timeout` is a ceiling, not a cost: a test that finishes at tick 10 with `Timeout = 600` costs ten ticks. Set it high enough that a slow machine does not fail spuriously.

## Escapes

An entity of a test's own that leaves its box is recorded, not punished. Leaving may be exactly what the test is watching, and dragging it back would change the behaviour under test, so an escape never fails anything. It does appear in the report, as `<system-out>` on that test's `<testcase>`:

```xml
<testcase name="A_slime_falls" classname="MyModTests.SlimeTests">
  <system-out>NPC 3 left the box at tick 40</system-out>
</testcase>
```

That note is usually the explanation for a neighbouring box behaving oddly a few tests later, which is otherwise a very hard thing to work out. Something that was never the test's to begin with is a different matter: that is contamination, and it errors the test, because a box someone else was in cannot honestly be said to have tested anything.

## Retained boxes, and tests that never ran

A test that fails keeps its box. The tiles it placed, the entities it spawned and whatever state it left behind all stay exactly as they were, so you can load the world and go and look. That is what `KeepFailedBoxes` is for, and it is on by default.

Retained boxes are never recycled. Nothing reuses that ground to keep the run moving, because doing so would destroy the evidence the retention existed for.

The consequence is that a run with many failures can run out of room. When that happens the tests that could not be given a box are reported as **blocked**:

```
1324 tests: 1290 passed, 9 failed, 0 errored, 25 blocked, 0 skipped
```

Blocked is its own outcome, distinct from the other three:

| Outcome | Means |
|---|---|
| Failed | An assertion did not hold. The subject is wrong. |
| Errored | The test threw, or could not run properly. The test is wrong. |
| Skipped | Somebody decided not to run it. |
| **Blocked** | It never got its turn. Says nothing about the subject either way. |

A blocked test's message names what is holding the space and what to do about it:

> This test never ran: the arena had no box for it. 49 of 49 slots are retained from earlier failures and are never reused, so that the state a failing test left behind survives for you to go and look at. Inspect them, then rerun. Set `KeepFailedBoxes` to false to give that ground up instead.

**A run containing blocked tests fails**, even when everything that actually ran passed. A suite that quietly stopped running part of itself has not established what it was asked to establish, and reporting success would be a lie of omission. In the JUnit report a blocked test is written as an `<error type="Testaria.Blocked">` rather than as `<skipped>`, so that CI reaches the same verdict the runner does; every CI system treats skipped as harmless.

## Running it

```
scripts/run-all.sh
```

Five gates, fastest-failing first: the core self-tests, the green path (the self-test mod must pass in a live headless server), the red path (deliberate failures must be reported as failures), fresh worlds (tests asking for an untouched world get one), and calibration against ExampleMod. Green alone proves little, since a framework that cannot report failure looks exactly like one that works.

Calibration needs `scripts/build-examplemod.sh` to have been run once.

Individual gates:

```
dotnet test                             # core only, no game needed
BLANK=1 scripts/run-tests.sh            # the self-test mod in a live server
FILTER='Zombie|Skeleton' BLANK=1 scripts/run-tests.sh  # regex; just the matching tests
MODE=list BLANK=1 scripts/run-tests.sh  # list tests without running them
scripts/check-red.sh                    # prove failures are reported as failures
scripts/run-fresh.sh                    # a dedicated server per [FreshWorld] test
```

`FILTER` is a regular expression, matched case-insensitively against both a test's name and its `Class.Name`. A bare fragment works as you would expect, since an unanchored regex search is a substring match, and alternation (`Zombie|Skeleton`) and exclusion (`^(?!.*Slow)`) are available when you want them. One trap: a name is not a pattern, so escape one before using it as an exact filter, because nested types contain `+`.

`[FreshWorld]` is worth a word. A runner sharing its world with other tests cannot honestly claim to have given one a fresh world, so an ordinary run reports such tests as **skipped**. `run-fresh.sh` gives each a process and a world of its own, and then they run. It is slow by construction, one server start per test, which is the price of the isolation they asked for.

`run-tests.sh` provisions a scratch save directory, drops the `.tmod` files in, launches a headless server on its own virtual display, pipes a console command, and maps the JUnit report to an exit code. It never touches a real installation's mods, worlds or players.

Still to come: Tier 3 (multi-process netcode and UI) and parallel box execution.

## Building against tModLoader

`Testaria.Core` and its tests need nothing but the .NET SDK. The `Testaria` mod project needs a tModLoader install on the **1.4.5 line** (`net10.0`, C# 14).

The build looks for one automatically in the usual Steam library locations; set `TML_PATH` to point elsewhere. Without an install, the mod project skips building the `.tmod` and says so, rather than failing, so a checkout with no game still builds and the core's tests still run.

To get 1.4.5 on Steam: tModLoader, gear icon, Properties, Betas, enter the password `iamacontributor` to unlock the branch, then select **`1.4.5-dev`**. Note that the `preview-*` branches are **not** 1.4.5, they are the monthly CI channel on the 1.4.4 line and install `net8.0` with `LangVersion 12.0`. See [tModLoader issue #5070](https://github.com/tModLoader/tModLoader/issues/5070).

## License

MIT. See [`LICENSE`](LICENSE).
