# Testaria

Unit, integration, and gameplay testing for Terraria mods built on tModLoader.

> **Status: pre-alpha.** Nothing here is usable yet. The design is settled (see [`PLAN.md`](PLAN.md)); implementation is in progress, starting from the game-independent core outward.

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

## Building

The core and its tests need only the .NET SDK:

```
dotnet test
```

Later tiers need a tModLoader installation. See `PLAN.md` section 6 for the version targeting decision.

## License

MIT. See [`LICENSE`](LICENSE).
