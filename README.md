# Testaria

Unit, integration, and gameplay testing for Terraria mods built on tModLoader.

> **Status: pre-alpha.** No tier runs end to end yet. The design is settled (see [`PLAN.md`](PLAN.md)), the game-independent core is built and self-tested, and the project now compiles against tModLoader 1.4.5.

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

The single most important rule the framework enforces is the **Tier 0 boundary**: the moment a test touches `Main`, `ModContent`, or `ContentSamples`, it depends on state that only a completed load pass establishes. In a bare test host those statics are default-initialized rather than absent, so such a test will often pass silently against garbage. `Testaria.Core` therefore carries no reference to tModLoader at all, which makes that boundary structural rather than advisory.

## Repository layout

```
src/
  Testaria.Core/        Game-independent core. No tModLoader reference, by design.
  Testaria.Game/        The tModLoader-facing half. Needs a 1.4.5 install to build.
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

Still to come: the runner that ties discovery, the arena, and the scheduler together, and everything in Tiers 1 through 3, which needs the game.

## Building against tModLoader

`Testaria.Core` and its tests need nothing but the .NET SDK. `Testaria.Game` needs a tModLoader install on the **1.4.5 line** (`net10.0`, C# 14).

The build looks for one automatically in the usual Steam library locations; set `TML_PATH` to point elsewhere. Without an install, `Testaria.Game` compiles to an empty assembly and says so, rather than failing, so a checkout with no game still builds and the core's tests still run.

To get 1.4.5 on Steam: tModLoader, gear icon, Properties, Betas, enter the password `iamacontributor` to unlock the branch, then select **`1.4.5-dev`**. Note that the `preview-*` branches are **not** 1.4.5, they are the monthly CI channel on the 1.4.4 line and install `net8.0` with `LangVersion 12.0`. See [tModLoader issue #5070](https://github.com/tModLoader/tModLoader/issues/5070).

## License

MIT. See [`LICENSE`](LICENSE).
