# Testaria

Unit, integration, and gameplay testing for Terraria mods built on tModLoader.

> **Status: pre-alpha.** No tier runs end to end yet. The design is settled (see [`PLAN.md`](PLAN.md)) and the game-independent core is built and self-tested; the in-game layer is blocked on a toolchain question, noted below.

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

Still to come: the tick scheduler and `Wait` primitives, the runner, and everything in Tiers 1 through 3, which needs the game.

## Known blocker: which tModLoader to build against

`PLAN.md` section 6 settles on targeting tModLoader's 1.4.5 line. That line is not yet obtainable as a prebuilt artifact:

- The Steam release of tModLoader is still the 1.4.4 line (`net8.0`, C# 12), even though Terraria 1.4.5.8 itself has shipped.
- No GitHub release carries a 1.4.5 asset; the release tags all come from the 1.4.4 branch.
- 1.4.5 is available only by opting into the `1.4.5-dev` Steam beta branch, or by running tModLoader's `setup-cli.sh` to decompile and patch from source.

Nothing in `Testaria.Core` is affected, since it references neither. The in-game layer cannot start until this is resolved one way or the other.

## Building

The core and its tests need only the .NET SDK:

```
dotnet test
```

Later tiers need a tModLoader installation. See `PLAN.md` section 6 for the version targeting decision.

## License

MIT. See [`LICENSE`](LICENSE).
