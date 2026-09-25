# A testing framework for Terraria mods: naming, scope, packaging, and distribution

Status: implemented through section 8.6h, with section 8.4 done as well. All four tiers run green in a live headless game, every artifact in the matrix is built, the framework has been calibrated against the published 1.4.5 ecosystem rather than only against ExampleMod, and nothing has been published to any channel. Section 0.1 is the status snapshot, section 8 records what each milestone establishes, and sections 8.7, 8.8 and 8.9 are what remains. Written against tModLoader on the `1.4.5` line; the calibration in section 8.6e ran against the `1.4.5-dev` Steam build `1.4.5.8+9999.0|2026.07|1.4.5|dev`, commit `39e7995f`. Paths given below as `patches/...` are relative to a [tModLoader](https://github.com/tModLoader/tModLoader) checkout.

## 0. The short version

- **It is not one product, it is four tiers** across two execution environments. Tier 0 (pure unit) runs in a normal `dotnet test` host. Tiers 1 through 3 (loaded, world, multi-process) must run inside a patched tModLoader process. Conflating them is the main design trap.
- **It is not one artifact, it is five.** tModLoader mods cannot consume NuGet, and .NET test projects cannot consume `.tmod`. The in-game runtime ships as a `.tmod`; the developer-facing toolchain ships as NuGet packages. One brand, two package systems.
- **Recommended brand: `Testaria`**, a coined portmanteau reused across the mod internal name, root namespace, NuGet ID prefix, and repo name. This is the only genuinely irreversible decision in the plan.
- **Primary distribution is GitHub Releases plus nuget.org, not the Steam Workshop.** The Workshop is a player channel, it cannot express prerelease versions, and test mods are never meant to reach players.
- **Boxes are leased and recycled, so the arena scales with concurrency, not suite size.** Two kinds: banded boxes inside one layer, and spanning columns for tests whose subject *is* a layer boundary. Isolation needs four layered mechanisms (geometry, an ownership warden, pool budgeting, declared global effects), all provisional until a real test corpus can adjudicate them (section 2.4).
- **The first milestone is Tier 1, not Tier 0**, because Tier 0 already works with a stock MSTest project and proves nothing new.
- **Publication is gated on tier 3, not on a date.** Preparation for section 5 begins once the earlier holes are filled, but nothing is published anywhere until tier 3 works and has worked examples against a real mod. CI for the game tiers ships in the same change as the first GitHub release, before nuget.org sees anything (section 5.1).

## 0.1 Where this stands

Measured on 2026-09-24 against the checkout this document lives in, by running the gates rather than by reading the code, and by section 8.6e against six foreign mods.

| Piece | State |
| --- | --- |
| Tier 0 | Works, and is defended. `Testaria.Core` plus a stock `dotnet test` project; 512 core, 76 tool, and 14 analyzer self-tests pass in under two seconds. Section 8.6e records the honest limit: a mod's own code lives in a mod assembly, so for most mods tier 1 is the practical floor |
| Tiers 1 and 2 | Work. Discovery, the tick scheduler, the arena, the blank world, the ownership warden, a test player, parameterised cases, filtering, pacing, stepping, per-test seeds, registered teardown, placing a tile as a player does, and restoring the ground all run inside a live headless server. The self-test mod reports 57 tests; the ExampleMod calibration suite reports 927; four foreign mods report 174 between them |
| Tier 3 | Works, sections 8.6a, 8.6c and 8.6e. A client process joins a real server; fourteen self-tests, three worked examples against ExampleMod, five replication tests against InnoVault and three layout tests against SilkyUI pass. A test mod can register questions the client answers, so a mod's own synced state is reachable, and so is anything else that process can do: two self-tests read back pixels the GPU drew. Rendering **tooling** is section 8.9; input is still unexplored |
| Artifact A, the `.tmod` | Built, loading, and exercised by every gate |
| Artifact E, templates | Scaffolded under `templates/`, neither packed nor published |
| Gates | Eight by default, run by `scripts/run-all.sh`: the core suites, the green path, the red path, the packages consumed as packages, the templates generated and then built and run, tier 3 both with and without a client, fresh worlds, and the ExampleMod calibration. A ninth, the arena load test, is deliberate (`RUN_LOAD=1`); its most recent run, the first since teardown began restoring the ground, is green at 308 boxes with a slowest restore of 28.9 ms. The tier 3 gate selects by tier out of the report rather than by class name, which section 8.6e explains at some length |
| Artifact D, the CLI | Built, section 8.5c. `testaria run` provisions, runs, reports, and exits with a code, on any platform the SDK runs on |
| Artifacts B and C | Built, section 8.5e. B wires a test project against an install; C runs a suite from MSBuild and carries the CLI inside itself |
| CI | Two halves. GitHub Actions covers the core tiers on three operating systems. The game tiers run locally, under `scripts/ci-local.sh`: preflight, provisioning, every gate strictly, a kept run directory, a lock, and a commit history, on a timer (section 8.6h). The hosted half of the game tiers still ships with the first GitHub release (sections 8.3a and 8.7), and now has a recipe to port rather than to invent |
| Publication | Nothing published to any channel, by design (section 5.1) |
| Section 2.2 mitigations | All three, as of section 8.5a: the core carries no tModLoader reference, the `TSTA001`/`TSTA002` analyzer ships in the `Testaria.Core` package, and `[RequiresLoadedGame]` plus `GameState.Require` cover what an analyzer cannot see |
| Seed control (risk 5) | Done, section 8.5b. Every test is seeded from its own identity, the seed is in the report, and `[Seed]` pins a particular roll |
| The five unmeasured numbers (risk 3) | All five measured (sections 8.5d and 8.5f). The box default and the quarantine are now calibrated numbers; the gutter stays a deliberate floor; the column region split has no corpus to calibrate against and says so |

Sections 8.4, 8.5 and 8.6 are finished, and with them every hole that stood before distribution work. What remains is sections 8.7 and 8.8, the publication sequence, which are gated on decisions and secrets that are not the code's to supply (section 5.1), and section 8.9, rendering, which is deliberately after both. Section 8.6h takes the CI half of 8.7 as far as it can go without a release: the game tiers now run unattended here, on a timer, so what 8.7 ships is a port rather than an invention.

Section 8.6e is the one to read before trusting any of the above. Six mods nobody here wrote produce sixteen findings between them, three of which no existing gate catches and two of which exist *because* of how a gate is written.

## 1. Constraints this plan rests on

Everything in this section was verified against a tModLoader checkout or the live ecosystem, with file and line references where applicable.

### 1.1 tModLoader constraints

1. **Mods reference assemblies by path, never by NuGet.** `tMLMod.targets` (`patches/tModLoader/Terraria/release_extras/tMLMod.targets:80-81`) injects `<Reference>` items for `tModLoader.dll` and every DLL under the install's `Libraries/`. There is no `PackageReference` path into a mod build.
2. **Only `dllReferences` DLLs get packed into the `.tmod`.** `ModCompile.cs:294-297` copies each `dllReferences` entry into `lib/<Name>.dll` inside the archive. NuGet restore output is not packed. A mod that wants a third party managed assembly must place it in `lib/` and name it in `build.txt`.
3. **Mod references are transitive for compilation.** `ModCompile.cs:434-446` shows that a mod naming another mod in `modReferences` compiles against that mod's assembly *and* against every DLL in that mod's `lib/`. A library mod is therefore a legitimate way to distribute both an API and a bundled dependency.
4. **Mod assemblies load from memory into a per-mod `AssemblyLoadContext`.** `AssemblyManager.cs:24`, `83-87`. Three consequences: a stock xUnit, NUnit, or VSTest runner cannot host in-process (they enumerate assemblies on disk); in-game discovery must be reflection over `AssemblyManager.GetLoadableTypes`; and any static state the framework registers must be torn down in `Unload()` or it pins dead load contexts across reloads (`AssemblyManager.cs:177`, `196`).
5. **There is a sanctioned way to build a non-mod project against tModLoader.** Setting `BuildMod=false` and `OutputTmlReferences=true` keeps the `<Reference>` items but skips the `.tmod` packaging target. tModLoader uses this for its own analyzer test project, `tModCodeAssist/tModCodeAssist.Tests/tModCodeAssist.Tests.csproj:3-4`, which is an MSTest project. Caveat: `TargetFramework` and `LangVersion` are only set when `BuildMod=true`, so such a project must set them itself.
6. **tModLoader has no house test framework and no first-party test-authoring API.** Its own suite is MSTest (`test/tModLoaderTests.csproj`), while `tModPorter` uses NUnit (`tModPorter/tModPorter.Tests/tModPorter.Tests.csproj`). The closest thing to integration testing in the repo is `test/Test Local/Mod Sources/`, a set of hand-built failure-case mods (`BuildFail1`, `LoadFail`, `RuntimeError`, `ServerHang`, `UnloadFail`, and others) that a human runs manually. The niche is genuinely empty, and you are free to pick your own conventions.
7. **`Terraria.Testing` is already a vanilla namespace.** It exists at `patches/tModLoader/Terraria/Testing` and holds `PacketHistory`. Do not root a namespace there.
8. **Local `.tmod` files load without Steam.** `ModOrganizer.cs:38` sets `modPath = Path.Combine(Main.SavePath, "Mods")`, and `ModOrganizer.cs:89` loads every `*.tmod` in that folder alongside Workshop mods. Enablement is a plain `Mods/enabled.json` (`ModOrganizer.cs:748`). A CI installer is therefore a few lines of shell: download, copy, write JSON.
9. **Launch parameters available to a harness**, from `patches/tModLoader/Terraria/`: `-server`, `-build`, `-savedirectory`, `-tmlsavedirectory`, `-nosteam`, `-eac`, `-define`, `-unsafe`, plus the vanilla dedicated server arguments (`-config`, `-world`, `-worldpath`, `-autocreate`, `-worldname`, `-seed`, `-players`, `-port`, `-password`). Separately, `-ciprep`, `-publishedmodfiles`, and `-uploadfolder` (`WorkshopSocialModule.TML.cs:436-462`) exist specifically to prepare a Workshop upload from CI via SteamCMD.
10. **The dedicated server is the headless target.** `-server` runs `Main` with `dedServ` set and no `GraphicsDevice`. It needs no display, which makes it the correct CI target, though it idles rather than simulating until a harness makes it tick (section 8.1a). Only render, UI, and input tests need a client, and those need a virtual framebuffer.
11. **Published mods are sandboxed by policy.** `ModUploadRules.md` rule 2 forbids accessing or modifying files outside the tML save and config directories and forbids launching external programs. Rule 3 forbids obfuscation and native libraries. This binds any runtime you publish: test results must be written under the save path, and the outer harness collects them from there.
12. **`build.txt` versions are 2 to 4 integers, with no SemVer prerelease tags.** `modReferences = Name@1.2` expresses a minimum version and is the only compatibility lever the Workshop gives you.
13. **`side` determines whether players are forced to download your library.** A `Both` library mod that another mod hard-references becomes a mandatory download for every player of that mod. `NoSync` is optional on both sides and never downloaded.
14. **The 1.4.4 and 1.4.5 lines are a hard fork of the build target.** 1.4.4 builds mods against `net8.0` with `LangVersion 12.0`; 1.4.5 moves to `net10.0` and `LangVersion 14.0` (`tMLMod.targets:16-17`) and mods are not backwards compatible.

### 1.2 C# and .NET conventions that apply

1. **NuGet package IDs** should be namespace-like dot notation, owner-scoped, and matched to the namespace the code uses. Hyphens and underscores are discouraged. Microsoft's guidance is `Company.Product.Feature`.
2. **Established OSS testing libraries own an unprefixed root** rather than a vendor prefix: `xunit`, `NUnit`, `Serilog`, `FluentAssertions`. A distinct brand name is enough to justify claiming `Brand.*`, and NuGet ID prefix reservation exists to protect it once claimed.
3. **Role suffixes are conventional** in the last segment: `.Sdk`, `.Analyzers`, `.Tasks`, `.TestAdapter`, `.Templates`.
4. **There is no official naming convention for a .NET tool package ID.** Microsoft's own tutorial documents only the mechanics (`PackAsTool`, `ToolCommandName`, `PackageOutputPath`) and states no convention for `PackageId`. The `dotnet-` prefix is explicitly *not* required: it is a convention some authors use to signal the package is meant for use with the `dotnet` command rather than as a standalone tool or library reference. The invocation name comes from `<ToolCommandName>`, independent of the package ID. The de facto landscape is genuinely split: `.Tool` (`Cake.Tool`, `GitVersion.Tool`), `.Cli` (`Swashbuckle.AspNetCore.Cli`), `.Console` (`coverlet.console`), and bare or prefixed (`dotnet-ef`, `csharpier`).
5. **JUnit XML is the universal CI test report format**, understood natively by GitHub Actions, GitLab, Jenkins, and TeamCity. TRX is the .NET-native alternative but is narrower.

### 1.3 Prior art worth stealing from

- **Minecraft GameTest** (Mojang, and the Forge and NeoForge ports) pairs each test with a *structure* and a *bounding box*: the test runs inside a reserved region of the world, so tests do not contaminate each other and failures are visually inspectable in place. That idea translates directly to Terraria as a reserved rectangle of tiles.
- **Fabric Loader JUnit** (`net.fabricmc:fabric-loader-junit`) is the split-environment precedent: a JUnit plugin for unit tests that need the loader's classloading, separate from GameTest for gameplay tests. Fabric's docs are explicit that these are two tools for two jobs, which is the same split proposed here as Tier 0 versus Tiers 1 through 3.
- **SpongePowered/McTester** is the cautionary tale of an integration test framework for a game that did not survive its platform's churn.
- **`gold-meridian/tml-build`** is the closest ecosystem neighbor: an MSBuild SDK for tModLoader shipped on NuGet as `Tomat.Terraria.ModLoader.Sdk`, which installs tModLoader itself for CI and packages `.tmod` on build. It is a possible foundation for the toolchain tier, but note it is **AGPL-3.0**, so vendoring code from it would be viral. Interoperate, do not copy.

### 1.4 Where to read the game

Two sources cover the game completely between them, and neither requires running tModLoader's `setup-cli.sh`:

- **Vanilla behaviour**: a decompile of the shipped game, per version, covering `Terraria`, `TerrariaServer`, and `ReLogic` along with the resources extracted from them. Any of the usual C# decompilers produces one from an install; tModLoader's `setup-cli decompile` produces the same thing in the form its patches expect.
- **tModLoader's own additions**: `patches/tModLoader/Terraria/` in a tModLoader checkout holds TML's added files in full rather than as diffs, so it reads directly.

Claims about game internals in this document are checked against the decompiled C#, including the two load-bearing ones: the server loop's `Netplay.HasFullyConnectedClients` gate around `Update`, and `NPC.CheckActive` iterating all 255 player slots so that nothing survives when none are active. Reading the decompile is much faster than reading IL; prefer it.

Note the split when it matters: the decompile is *vanilla*, while tModLoader is a patched rebuild that inserts its own hooks, such as the `NPCLoader.CheckActive` call that makes the despawn fix possible at all.

## 2. Scope

### 2.1 The four tiers

| Tier | Name | Runs in | Requires | Can test | Wall clock |
| --- | --- | --- | --- | --- | --- |
| 0 | Unit | `dotnet test` host, outside the game | tML assemblies as compile references only | Pure functions, math, geometry, coordinate conversions, parsers, `TagCompound` round trips, anything with no dependency on loader state | Milliseconds |
| 1 | Loaded | tModLoader `-server`, no world | The loader has completed a load pass | Content registration, `ModContent` ID resolution, recipe graphs, ID sets, `ContentSamples` data, config serialization, localization key coverage, cross-mod `Mod.Call` contracts | Seconds, one process start |
| 2 | World | tModLoader `-server`, world loaded | A world and a running tick loop | NPC AI over ticks, world generation passes, tile framing and multi-tile placement, drop tables, buff and debuff timers, player mechanics, save and load round trips | Tens of seconds |
| 3 | Multi-process | Server plus one or more clients | Two or more processes; a virtual framebuffer for any client | Netcode and sync, `ModPacket` round trips, `netMode` branching, UI, rendering, input | Minutes |

### 2.2 The boundary that matters most

**Tier 0 must not be allowed to pretend it can do Tier 1.** The moment a test touches `Main`, `ModContent`, `ContentSamples`, or `Language`, it depends on state that only a completed load pass establishes. In a bare test host those statics are default-initialized rather than absent, so such a test will often *pass silently against garbage*. That is the single most likely way this framework produces false confidence, and it is worse than having no framework at all.

**This is measured, not predicted.** A probe project using the `BuildMod=false` pattern against tModLoader 1.4.5, with no game started, gives:

| Touched | Result |
| --- | --- |
| `ItemID.CopperShortsword`, `ItemID.Count` | Works. Consts are inlined at compile time |
| `TileID.Sets.Falling.Length` | Works, returns 754 |
| `new Item()` | Works, constructs fine |
| `Main.maxTilesX`, `Main.worldSurface` | **Throws** `TypeInitializationException` |
| `ContentSamples.ItemsByType.Count` | **Returns 0** |
| `ContentSamples.NpcsByNetId.Count` | **Returns 0** |
| `new Item().Name` | **Returns `""`** |
| `ModLoader.Mods.Length` | **Returns 0** |
| `Lang.GetItemNameValue(3507)` | **Returns `""`** |
| `ItemID.Sets.Deprecated.Length` | **Returns 6196**, the vanilla count, never resized for mods |

The split matters more than any individual row. `Main` fails *loudly*, so the obvious mistake is self-correcting. Everything else answers anyway, with an empty collection, an empty string, or a vanilla-sized array. A test asserting `Assert.Empty(...)` over `ContentSamples`, or comparing against `ItemID.Count`, or checking a localized name, goes **green while proving nothing at all**.

So the danger is not `Main`. It is `ContentSamples`, `ModLoader.Mods`, `Lang`, `ModContent`, and the `*ID.Sets` arrays, and mitigations should target those.

Mitigations, in order of preference:

1. Ship Tier 0 helpers in a package that does **not** transitively reference `tModLoader.dll`, so the dangerous types are simply not in scope. This is the clean answer where it is achievable.
2. Where a reference is unavoidable, ship a Roslyn analyzer that errors on use of the loader-dependent surface from a Tier 0 assembly. tModLoader already ships analyzers this way (`tMLMod.targets:86-87`), so the pattern is familiar to users. The measurements above give it a concrete target list: `ContentSamples`, `ModLoader.Mods`, `Lang.*`, `ModContent.*`, and `*ID.Sets.*`. `Main` need not be on it, since it already throws.
3. At minimum, document the boundary loudly and provide a `[RequiresLoadedGame]` marker that fails fast rather than silently.

**All three are built** (section 8.5a). The analyzer is `TSTA001`, the propagation rule that keeps the marker from being a mere silencer is `TSTA002`, and the runtime guard is `GameState.Require`.

### 2.3 The tick problem, and what Tier 2 must look like

Terraria is a fixed 60 Hz tick loop, not a request and response system. A gameplay assertion is inherently "over N ticks", not "the return value of this call". So the core abstraction of Tier 2 cannot be a plain `void Test()`.

Recommended shape: **an `IEnumerator` coroutine that yields ticks.**

```csharp
[GameTest(Timeout = 600)]  // ticks, so 10 seconds
public IEnumerator ZombieDiesToSword(TestContext ctx)
{
    var npc = ctx.SpawnNPC(NPCID.Zombie, ctx.Center);
    var player = ctx.SpawnPlayer();

    player.UseItem(ItemID.CopperShortsword);
    yield return Wait.Until(() => !npc.active);

    Assert.False(npc.active, "zombie should be dead");
}
```

This reads naturally in a fixed-step loop, composes (`Wait.Ticks(60)`, `Wait.Until(pred)`, `Wait.ForBossDespawn()`), and gives a natural place to hang a per-test tick timeout. The `async`/`await` alternative over a tick scheduler is more modern but introduces a synchronization context that has to be defended against reload, and it invites accidental thread hops into code that is not thread safe. Coroutines are the safer fit.

Beyond the coroutine, Tier 2 needs **determinism**: Terraria drives behavior through `Main.rand` and `WorldGen.genRand`, so without explicit seed control gameplay tests will flake. Seed control belongs in the framework from day one, not retrofitted. It also needs isolation, which is involved enough to get its own section.

### 2.4 Spatial isolation, in detail

#### Box size is declared per test, not fixed

One global box size cannot work, because Terraria test footprints span orders of magnitude. A recipe assertion needs no space at all. A zombie AI test needs tens of tiles. An Eye of Cthulhu fight ranges hundreds of tiles and teleports. A world generation pass test wants a whole world.

So: `[GameTest(Width = 120, Height = 80)]` in tile units, over a modest default (80 by 48 is a reasonable starting guess, to be calibrated once real tests exist). Declared sizes are rounded up into a handful of **size classes** (48x32, 96x64, 192x128, 384x256) so that a freed box is interchangeable with any other box in its class, which is what keeps the arena from fragmenting as a suite grows.

A small default is only safe because the warden below turns an under-declared box into an explicit failure rather than silent cross-talk. Get that ordering right and authors can leave the size alone until something escapes.

#### Depth is semantic, so boxes tile horizontally

This is the Terraria-specific constraint with no Minecraft analogue. Terraria's layers are defined by absolute Y derived from `Main.maxTilesY`, and depth determines spawn pools, biome, background, music, and ambient lighting. Two boxes stacked vertically are therefore not equivalent test environments.

Lay boxes out **horizontally in rows, one row per required depth band**, and let a test declare its layer (`[GameTest(Layer = Layer.Cavern)]`). Free lists are therefore keyed by (layer, size class), not by size alone.

That covers the common case, where a test lives entirely inside one layer. It is not the only case.

#### Some tests must span bands, and those boxes are a different shape of problem

A real class of tests exists precisely to exercise what happens *at* a layer boundary: falling from the surface into the cavern and taking the right damage, a hellevator dug through every layer, spawn pools and background and music switching with depth, ore distribution across a worldgen pass, anything asserting on `Main.worldSurface` or `Main.rockLayer` behavior. None of these fit in a banded box, because the thing under test is the boundary itself.

So there are **two kinds of box**, not one:

- A **banded** box fits inside a single layer, is freely interchangeable within its (layer, size class) free list, and is the common case.
- A **spanning** box declares a band *range* and occupies a vertical column that crosses real boundaries.

**A spanning box is anchored to world geometry, not sized by the test.** Its height is dictated by `Main.worldSurface`, `Main.rockLayer`, and the underworld boundary, all derived from `Main.maxTilesY`. The test declares which bands it needs and how wide it wants to be; the arena computes the height. Width is the only dimension the author actually controls:

```csharp
[GameTest(Spans = Band.Surface | Band.Underground | Band.Cavern, Width = 160)]
```

Declaring a *band range* rather than a raw depth range is deliberate. "400 tiles below the surface" means different things on small and large worlds, whereas "surface through cavern" always contains the boundary the test cares about, whatever the world size.

**Give spanning boxes their own arena region.** The tempting layout, letting a column punch down through the banded rows, is a trap: a column would then need to acquire an aligned slot in every row it crosses, simultaneously, which is textbook hold-and-wait and deadlocks the moment two columns are allocated concurrently. Partition the world horizontally instead, into a banded region where rows pack densely and a **reserved column region** for spanning boxes. That costs some idle space and buys zero fragmentation and zero cross-kind interference. Since the arena is sized by concurrency and a small world is roughly 4200 tiles wide, the space is effectively free. If partitioning ever proves too wasteful, the fallback is a fixed top-to-bottom acquisition order with release-and-retry rather than hold-and-wait.

Columns also need **their own size classes**, keyed by (band range, width class) only, since height is world-determined rather than author-chosen.

**Recycling a column is far more expensive.** A full-height column on a small world is 1200 tiles tall; at 160 wide that is 192,000 tiles to snapshot and restore, against 3840 for an 80 by 48 banded box, roughly a factor of 50. Still only a few megabytes, so not prohibitive, but it does mean spanning should be a deliberate declaration rather than a default, and quarantine for a column takes correspondingly longer to go quiet.

**There is a crossover with `[FreshWorld]`.** A test wanting full height *and* substantial width is asking for a meaningful fraction of a world, at which point generating a fresh one is simpler and possibly cheaper than leasing and scrubbing a column. Where that crossover actually sits is worth measuring rather than guessing.

**Spanning tests carry a reproducibility hazard that banded tests do not.** Band boundaries derive from `Main.maxTilesY`, so the arena must pin its world size (small is fastest to generate and sufficient) and record it. More subtly, special world seeds change layer *semantics* rather than just geometry: tModLoader's own documentation notes that under `Main.remixWorld`, the "Don't dig up" seed, tiles that are technically underground may be treated as overground (`GlobalBlockType.cs:105`, `ModBlockType.cs:152`). A band-spanning test is therefore sensitive to the arena world's seed as well as its size, and both belong in the test report alongside the RNG seed.

#### Boxes are leased and recycled, never statically assigned

A test suite grows without bound; the world does not. So boxes of both kinds are **leased from an arena and returned**, not assigned once at suite start. The reframing that matters: **arena size scales with peak concurrency, not with test count.** Running sequentially, the whole arena is about two boxes, one live and one in quarantine. The roughly 40 boxes a row can hold was never a ceiling on suite size, it is a ceiling on parallelism.

Teardown has to actually restore everything the box touched, and Terraria hides state in more global, position-keyed structures than is obvious:

1. **Tiles.** Snapshot the rectangle before the lease (type, wall, liquid, `frameX`/`frameY`, color, slope and flags) and blit it back on release. At 80 by 48 that is 3840 tiles, which is nothing.
2. **Owned entities.** The warden already tracks ownership, so release deactivates everything the box owns.
3. **Tile entities.** `TileEntity.ByID` and `ByPosition` are global and position-keyed. Entries inside the rectangle must be purged explicitly or they survive the tile restore, leak, and collide with the next tenant. This is the easiest leak to miss.
4. **Chests and signs.** `Main.chest[]` and `Main.sign[]` are global fixed pools keyed by position, so a test that places a chest consumes a global slot that release must free.
5. **The liquid update queue.** Queued liquid updates are global and position-keyed; drain the region's entries or the next tenant inherits flowing water.
6. **Lighting.** Recomputed from tiles, so the tile restore covers it, but with a frame or two of lag, which is one reason for the quarantine below.
7. **Housing.** A test that happens to build a valid room registers town NPC housing globally. An edge case, but a real one.

**Quarantine before re-issue.** Several effects outlive teardown by ticks: queued liquid, in-flight projectiles the warden is still reeling in, lighting propagation, despawn timers. A released box therefore sits in quarantine for N ticks, and the warden must observe it *quiet* before it goes back on the free list: no active entities in the rectangle, no queued liquid, tiles matching the snapshot. A box that will not go quiet is a framework bug worth reporting loudly rather than recycling silently.

**Failed boxes are exempt.** Preserving a failed test's box is the single best debugging affordance GameTest has, so `--keep-failed` retains the rectangle and puts its coordinates in the failure report, letting an author fly straight to the wreckage. Retained boxes consume arena, so cap them and recycle oldest-first past the cap.

**Overflow.** If the free list is empty and the arena has no room, block until a lease returns. Because arena size tracks concurrency rather than suite size, this should be unreachable in practice, so reaching it indicates a scheduler bug or a box that never went quiet, and should be a loud diagnostic rather than a silent stall.

#### Stopping a box from affecting its neighbors takes four layered mechanisms

Geometry alone cannot do it, and believing otherwise is how this framework would ship flaky.

**1. Geometry: the box plus a declared gutter.** Cross-boundary effects have wildly different radii, so a single gutter constant is wrong:

- `WorldGen.TileFrame` recurses one tile into neighbors, so placing a tile at the box edge reframes tiles outside it.
- Lighting propagates tens of tiles.
- Liquid flows, and its update queue is global.
- `Main.SceneMetrics` decides biome by scanning a rectangle of tiles around a player, and is the largest radius of the four. It is confirmed present and perspective-based (`GlobalTile.cs:112` refers to `Main.SceneMetrics.PerspectivePlayer`), but **its scan radius is not readable from a tModLoader checkout**: `SceneMetrics.ScanAndExportToMain` is unpatched vanilla and lives in the generated `src/`, which a fresh checkout does not contain. Measure it before fixing any gutter constant; section 8.5d does.

The practical rule is that the gutter is sized by the strongest effect the test actually asserts on. A tile-framing test needs two tiles. A biome test needs a gutter wider than the SceneMetrics radius, which probably makes biome tests exclusive in practice rather than boxed.

**2. An ownership warden.** Tag every entity a test spawns with its owning box, in an index-keyed side table hung off `GlobalNPC`, `GlobalProjectile`, and a `ModSystem`. Reconcile every tick: an owned entity found outside its box is a **test failure**, and an unowned entity found inside a box is removed.

The point is not that this prevents every escape. It is that it converts the failure mode from "the neighboring test is mysteriously flaky" into "test X's zombie left its box at tick 340", which is a diagnosis rather than a mystery. It also makes under-declared box sizes self-reporting.

**3. Pool budgeting, which is the constraint that gets missed.** Terraria's entity pools are global, fixed, and small: `Main.npc` 200, `Main.item` 400, `Main.projectile` 1000, `Main.player` 255, from `Main.maxNPCs` (`InitData.MaxNPCs`), `Main.maxItems`, `Main.maxProjectiles`, and `Main.maxPlayers`. On exhaustion the game does not raise; it silently fails to spawn or recycles a slot. So two parallel boxes can cause each other to fail in a way that is indistinguishable from a genuine mod bug. Any scheduler that runs boxes concurrently must treat pool slots as a budget and only co-schedule tests whose declared budgets sum to a safe fraction of each pool.

**4. Declared global effects, because much of the relevant state has no spatial component at all.** No box protects a neighbor from `Main.dayTime`, `Main.time`, `Main.moonPhase`, `Main.hardMode`, `Main.bloodMoon`, `Main.eclipse`, `Main.raining`, `Main.invasionType`, the `NPC.downed*` progression flags, spawn rate globals, or the shared `Main.rand` stream. A test declares what it mutates (`[Mutates(WorldState.Time | WorldState.Weather)]`); the scheduler serializes tests touching the same global and snapshots and restores it around each one; tests declaring nothing global parallelize freely.

#### The warden, as built

Mechanism 2 exists. Two properties of it are only observable in a running game.

**Contamination reports as an error, not a failure.** That maps onto a distinction the rest of the framework already makes: a failure says the subject is broken, an error says the test did not run properly. A box something else was in is exactly the latter, so a test whose assertions all passed is downgraded rather than trusted. Failing would blame a subject that may be fine; passing would claim coverage that did not happen. A genuine failure outranks the note, since the assertion message is the more useful of the two.

**The watcher has to run before the game updates its entities.** NPC updates, and with them `CheckActive`, run earlier than `PostUpdateEverything`, so a watcher standing there sees a world the game has already tidied. Measured with an intruder spawned from `PreUpdateEntities`: it is caught on **tick 1**, and an intruder that far from a player is gone within five ticks, so the window is the whole problem.

That generalises beyond this one case. Anything transient, and an intruder about to be despawned for being far from a player is the definition of transient, exists only in a narrow window of the tick, and a watcher outside that window sees nothing and reports everything as clean.

Escapes are recorded but deliberately not treated as contamination: an entity leaving its box may be exactly what a test is observing, and dragging it back would change the behaviour under test.

#### Parallelism is worth building

**The server ticks at real time.** Measured directly: 1001 ticks took 16.668 seconds, 60.1 ticks per second, 16.65 ms per tick. The pacing is `double num6 = 16.666666666666668` in `Main.DedServ_PostModLoad`, a *local variable*, so there is no field a mod can set; raising the rate would need an IL rewrite of that method body. **Treat a tick as 16.7 ms of wall clock and it will not surprise you.**

The cost of a gameplay test is therefore its tick count. A boss fight of a thousand ticks is seventeen seconds. Ten of them, run one after another, is nearly three minutes.

**Tests in separate boxes share one tick stream**, which is what makes parallelism pay here. Ten tick-bound tests of a thousand ticks each cost about 167 seconds sequentially and about 17 concurrently. That is the whole argument for mechanisms 3 and 4, pool budgeting and declared global effects: they exist to make concurrent boxes safe, and for a gameplay suite concurrency is the difference between a usable loop and an unusable one.

For a suite dominated by Tier 1 tests none of this binds: measured, 924 such tests run in 0.41 seconds, because about 99% of them never tick at all, and the wall clock is world generation and server startup rather than test execution. Both statements hold, of different suites, which is the distinction worth carrying.

#### Status of the four mechanisms

All four stay in the plan as the working design, but they are **hypotheses to be evaluated against real test surface, not settled commitments**. Their relative value is not knowable until there is a corpus of actual tests to observe: it is entirely possible that the warden subsumes most of what the gutter is for, that pool budgeting turns out to be the binding constraint long before geometry does, or that some fifth mechanism nobody has thought of is the one that matters. Build them, instrument them, and let the first real suite adjudicate.

Two things are worth noting in the meantime. Mechanisms 1 and 2 earn their keep even under sequential execution, since the gutter protects the *next* tenant's ground and the warden gives every failure a location you can fly to. Mechanisms 3 and 4 only bind once boxes run concurrently, so they can be built behind the parallelism switch without holding up a usable v1.

`[FreshWorld]` remains the escape hatch for tests that need genuine isolation, at the cost of a world generation.

### 2.5 Explicitly out of scope for v1

State these so the scope does not creep:

- Rendering **tooling**: image comparison, golden files, and visual diffing. A client query reaches a real graphics device and can read back pixels (section 8.9), so what is out of scope here is the tooling rather than the access.
- Input replay and recorded play sessions.
- Performance benchmarking and regression thresholds. (`test/EntityIteratorsPerformance` and friends in the tModLoader repo show this is a separate discipline with separate tooling.)
- Testing tModLoader itself. The audience is mod authors.
- 1.4.4 support (settled: 1.4.5 only).
- Parallel box execution. Sequential first; the arena, lease, and recycle machinery is built for it, but the scheduler is not (section 2.4).

## 3. Naming

### 3.1 The five layers a name has to survive

| Layer | Convention | Ecosystem examples |
| --- | --- | --- |
| Mod internal name (equals assembly name, folder name, and `.tmod` filename) | PascalCase, no dots, spaces, or separators. **Globally unique across the whole mod browser, first come, no owner prefix.** | `SubworldLibrary`, `ParticleLibrary`, `StructureHelper`, `ModLibsUI`, `SerousCommonLib`, `MagicStorage` |
| `displayName` in `build.txt` | Spaced and human, carries the branding | "Subworld Library", "Example Mod" |
| C# root namespace | Conventionally identical to the internal name | `ExampleMod`, `SubworldLibrary` |
| NuGet package ID | Dot notation, namespace-matched, owner-scoped or brand-rooted | `Tomat.Terraria.ModLoader.Sdk`, `QuickAssetReference.TModLoader`, `OTAPI.Upcoming.tModLoader` |
| GitHub repo | lowercase kebab is the .NET community norm | `tModLoader/tModLoader`, `gold-meridian/tml-build` |

The structural recommendation, independent of which word wins: **pick one brand token and reuse it verbatim at every layer.** The ecosystem has no consistent convention of its own (`Tomat.Terraria.ModLoader.Sdk` and `OTAPI.Upcoming.tModLoader` do not agree on anything), so internal consistency is what buys recognizability.

### 3.2 Candidates

| Candidate | Reads as | Collision risk | Verdict |
| --- | --- | --- | --- |
| `ModTest`, `TestFramework`, `TestHarness` | Generic | High. Unsearchable, invites squatting, reads as "a mod that is a test" | Reject |
| `tModTest`, `TMLTest`, `tModLoader.Testing` | Official TML team product | Implies endorsement that does not exist; discourteous to the team's naming space | Reject |
| `Terraria.Testing` | Natural namespace | **Already exists in vanilla**, holds `PacketHistory` | Reject |
| `TerrariaTestKit` | Literal and clear | Long; leans on Re-Logic's trademark harder than is comfortable for a NuGet ID | Weak |
| `Crucible` | Thematic, a vessel for trials, evokes the Hellforge | Heavy prior use in gaming (Destiny) and dev tooling; does not say "testing" unaided | Weak |
| `Touchstone` | A stone used to assay gold; mineral theme fits Terraria unusually well | Moderate in dev tooling; still does not say "testing" unaided | Viable |
| `ProvingGrounds` | Exactly what it is, and thematically an arena where things are tested | Low in this ecosystem | Viable |
| `Terratest` | Literal and immediately legible | **Gruntwork's Terratest** (a Go library for Terraform infrastructure testing) owns the bare search term. No legal issue, but real and permanent SEO friction for a tool whose audience arrives by search | Reject |
| `Testrarria` | Portmanteau of Test and Terraria | Free, but carries a spelling trap: the `rr` cluster makes it hard to type correctly and easy to get wrong in a `modReferences` line or a package ID, where a typo is a build error | Weak |
| `Testaria` | Portmanteau of Test and Terraria, coined and unambiguous | **Nothing found.** Nearest neighbor is `Tetrarria`, an unrelated and dormant ModDB Tetris clone, which is a different word | **Recommended** |

**Recommendation: `Testaria`.**

A coined word is the strongest option available here, and better than the literal `Terratest` for three reasons. It is *unambiguous in search*: a made-up token returns only you, which beats a descriptive name permanently outranked by an established tool in another language. It *sounds like what it is* without containing the trademark literally, which is safer than `TerrariaTestKit` while still reading as Terraria-adjacent on first hearing. And it *survives all five layers unchanged*, with no casing or separator negotiation.

`Testaria` over `Testrarria` on one practical ground: the doubled `r` is a spelling trap, and this token gets typed into `build.txt` `modReferences` lines and NuGet package IDs, where a misspelling is a build failure rather than a search miss. Fewer letters, one `r`, no cluster.

The one honest cost: the name does not contain the literal string "Terraria", so it will not match a naive substring search. That is what `displayName` and the NuGet `description` are for, and it is a first-week problem rather than a permanent one.

### 3.3 Concrete layout under `Testaria`

| Layer | Value |
| --- | --- |
| GitHub repo | `terraria-mod-Testaria` |
| Mod internal name | `Testaria` |
| `displayName` | `Testaria` |
| Description line | "Unit, integration, and gameplay testing for Terraria mods" (carries the literal keyword the name omits) |
| Workshop description headline | "Testing framework for tModLoader mods" |
| Root namespace | `Testaria`, with `Testaria.Assertions`, `Testaria.Runner`, `Testaria.World`, `Testaria.Net` |
| Public attributes | `[GameTest]`, `[LoadedTest]`, `[FreshWorld]`, `[Theory]`-equivalent `[GameTheory]` |
| NuGet ID prefix | `Testaria.*`, reserved on nuget.org |
| CLI command | `testaria` (see section 4.4.8 on why the package is not named `dotnet-testaria`) |
| Results directory | `<SavePath>/Testaria/` |

## 4. Packaging

### 4.1 The central tension

Mod projects cannot consume NuGet (section 1.1.1), and .NET test projects cannot consume `.tmod`. So the framework is necessarily split across two package systems. Trying to force one artifact to serve both is the packaging mistake to avoid.

### 4.2 Artifact matrix

| # | Artifact | Form | Contains | Consumed by |
| --- | --- | --- | --- | --- |
| A | `Testaria` | `.tmod` library mod, `side = NoSync` | The Tier 1 through 3 runtime: discovery, tick scheduler, assertions, world fixtures, result writer, console `ModCommand` | The game, loaded next to the mod under test |
| B | `Testaria.Unit` | NuGet library plus targets | Tier 0 helpers, and the `BuildMod=false` wiring that points a test project at a tML install | `MyMod.Tests.csproj`, run by `dotnet test` |
| C | `Testaria.Sdk` | NuGet MSBuild SDK or targets | A build-integrated entry point: provision, install A, build the mod, launch `-server`, run, collect results, fail the build on red | Mod repositories and CI |
| D | `Testaria.Tool` | NuGet dotnet tool, command `testaria` | CLI: `testaria run`, scratch save directory provisioning, world setup, `enabled.json` authoring, JUnit XML output | Humans and CI |
| E | `Testaria.Templates` | NuGet `dotnet new` template pack | Scaffolds a test mod plus a test project, with a **working placeholder test at every tier** (see 4.4.9) | Onboarding |

B through E are optional in v1 and can be collapsed; A and D are the minimum viable pair.

### 4.3 How a mod author consumes A

Two shapes, and the choice has real consequences for players:

**Recommended default: a separate test mod.** `MyModTests` is its own mod folder with `modReferences = MyMod, Testaria` and is never published. The player-facing `MyMod` carries no test code and no dependency on the framework, so no player is ever asked to download `Testaria`. This is the clean separation and it costs one extra folder.

**Alternative: tests inside the mod, conditionally compiled.** Tests live under `Tests/` in the mod, guarded by `#if TESTARIA`, with the define passed through `ExtraBuildModFlags` into the `-define` argument (`tMLMod.targets:102`). `buildIgnore = Tests\*` excludes the folder from a release build. Cheaper for a small mod, but it leaks test source into the shipped archive if `includeSource = true`, and requires `weakReferences = Testaria` so the release build does not force the dependency. Offer it, do not default to it.

### 4.4 Packaging decisions to lock in

1. **Do not host xUnit, NUnit, VSTest, or TUnit in-game.** Section 1.1.4 makes reflection-based runners impractical. Write discovery over `AssemblyManager.GetLoadableTypes` instead. But deliberately **mirror xUnit's vocabulary** (a `[Fact]`-shaped attribute, a `[Theory]`-shaped attribute, `Assert.*` with the same method names and argument order) so the surface is learnable in five minutes, and **emit JUnit XML** so CI already understands the output without a custom reporter.

    TUnit deserves its own note, because at first glance its architecture looks like a *better* fit than xUnit's: it discovers tests by Roslyn source generation at build time rather than by scanning assemblies at runtime, which sidesteps the `AssemblyLoadContext` problem entirely. It is nonetheless a worse fit. TUnit supports only Microsoft.Testing.Platform, and compiles a test project into an **executable that owns `Main`**. A `.tmod` is a library loaded into a process the game already owns, so that host has nowhere to live. TUnit is less embeddable than xUnit, not more.
2. **Reflection discovery rather than source generation, because the two build paths disagree.** Source-generated discovery is genuinely attractive on its merits: faster, no runtime scanning, and it would move malformed-test detection from discovery time to *compile* time, which is strictly better than reporting a `TestDiscoveryError` after the fact. It is ruled out by how `.tmod` files actually get built.

    `ModCompile.BuildMod` (`ModCompile.cs:385-414`) packages the csproj-built assembly only when `-eac` is passed, and otherwise falls through to `CompileMod`. `tMLMod.targets:102` always passes `-eac`, so an IDE or `dotnet build` yields a `.tmod` containing generator output. The in-game Mod Sources "Build" button, however, invokes `ModCompile` with no such launch parameter, so it takes the fallback path, and that path is `RoslynCompile` (`ModCompile.cs:526-548`), which builds a `CSharpCompilation` and calls `Emit` with **no `GeneratorDriver` at all**.

    A mod depending on a source generator would therefore build correctly from an IDE and silently produce a broken assembly when built in-game. That is the worst available failure shape, and reflection discovery behaves identically on both paths.

    This does **not** rule out the Tier 0 boundary analyzer proposed in section 2.2. An analyzer only emits diagnostics and never changes the emitted assembly, so one that does not run on the in-game path simply offers no advice there. **Analyzers degrade gracefully; source generators do not.**
3. **Content types must live in the mod's own assembly, not in a bundled library.** Autoloading enumerates `AssemblyManager.GetLoadableTypes(Code)` (`Mod.Internals.cs:71`), and `Code` is the mod's own assembly alone, not every assembly in its load context. So a `ModSystem`, `ModCommand`, or any other `ModType` shipped inside a `lib/` DLL is simply never discovered, silently.

    That fixes the shape of artifact A. The Testaria mod's own assembly carries the `ModSystem` and `ModCommand`; `Testaria.Core` rides along as `lib/Testaria.Core.dll`, which is fine because it contains no autoloadable types at all, only plain classes. Dependents naming `Testaria` in `modReferences` receive both assemblies as compile references (section 1.1.3), so a test mod can use the core's types directly.
4. **Keep bundled managed dependencies at zero if possible.** The mechanism exists (`lib/` plus `dllReferences`, and it is transitive to dependents per section 1.1.3), but every bundled DLL is another memory load on every reload and another surface under `ModUploadRules` rule 3.
5. **Write results only under `<SavePath>/Testaria/`.** Rule 2 requires it for anything published, and designing to it from the start keeps Workshop publication available as an option even if you never exercise it.
6. **B must set its own `TargetFramework` and `LangVersion`**, because `tMLMod.targets` only sets them under `BuildMod=true`.
7. **License MIT**, matching tModLoader and the ecosystem norm, and keeping the door open to upstreaming. Note again that `tml-build` is AGPL-3.0 and must not be vendored.
8. **Name the CLI package `Testaria.Tool`. This is a weak preference over `Testaria.CLI`, not a convention.** Per section 1.2.4 there is no official guidance and the ecosystem is split, so the only real constraint is that the ID stay under the `Testaria.*` root, which both candidates satisfy and `dotnet-testaria` would not (it would sit outside the ID prefix reservation and remain squattable).

    The tiebreaker for `.Tool`: this package will have sibling *library* packages under the same brand (`Testaria.Unit`, `Testaria.Sdk`, `Testaria.Templates`), and disambiguating the installable tool from the libraries is precisely the job `.Tool` was adopted for by Cake and GitVersion, both of which faced the identical situation. `.CLI` is weaker here because it is used for both tools and plain libraries, so it does not answer "can I `dotnet tool install` this?" on sight.

    The counter-argument is real and worth recording: `.CLI` reads better in prose, and nobody says "the Testaria Tool". That costs nothing, though, because prose can call it "the Testaria CLI" regardless of the package ID, and what anyone actually types is `testaria`, set by `<ToolCommandName>`. If `.CLI` is preferred on taste, nothing else in this plan changes.
9. **The template must ship a working placeholder at every tier, not just Tier 0.** A Tier 0-only template would actively teach the failure mode described in section 2.2, by implying that the default home for a test is the out-of-game project. It should scaffold one Tier 0 test, one Tier 1 test, and one Tier 2 boxed test, each with a comment stating *why it lives at that tier*, so the boundary is learned from a working example rather than from documentation nobody reads. The second payoff is diagnostic: environment setup is the hardest part of adopting this framework, and a scaffold that goes green end to end proves the tML install path, scratch save directory, and world provisioning all work before the author has written a line of their own. Tier 3 stays out of the default template, since it needs a second process, and gets its own opt-in template.

## 5. Distribution

| Artifact | Primary channel | Secondary | Rationale |
| --- | --- | --- | --- |
| A, the `.tmod` | **GitHub Releases** | Steam Workshop, optional and later | The audience is developers who already have a checkout. GitHub Releases can carry full SemVer tags, and CI can install with `curl` plus a copy into `<SavePath>/Mods/` plus a line of JSON (section 1.1.8). The Workshop cannot express prerelease versions and requires Steam plus either the in-game UI or SteamCMD with `-ciprep` |
| B, C, E | **nuget.org**, with the `Testaria.*` ID prefix reserved | GitHub Packages for prereleases | Standard .NET distribution |
| D | **nuget.org** as a dotnet global tool | | `dotnet tool install -g Testaria.Tool`, invoked as `testaria` |
| Documentation | **GitHub Pages built from `docs/`** via DocFX | Cross-link from the tModLoader wiki | A wiki lives in a separate `*.wiki.git` repo, so it cannot be versioned with the code, reviewed in a PR, or changed atomically with the API it documents. For a framework whose surface will churn with the 1.4.5 port, that drift is the predictable failure. `docs/` also lets contributions arrive as PRs, lets code samples be compile-checked in CI, and lets a merge be gated on a docs update. DocFX is the .NET-native generator and pulls API reference straight from XML doc comments, which the public attribute and assertion surface should carry anyway. tModLoader's own generated docs at docs.tmodloader.net set the precedent |
| Announcement | Terraria Community Forums thread, tModLoader Discord `#modding` | | Draft only; do not post without explicit instruction |

The asymmetry worth internalizing: **GitHub is the right primary for the `.tmod` precisely because local mod loading does not require Steam.** Publish to the Workshop only if you later want end users of published test mods to get dependency resolution automatically, which, if test mods are never published, you never need.

### 5.1 When any of this actually happens

The table above is the shape of distribution, not a schedule. The schedule is settled, and it is deliberately conservative about the public half:

1. **Fill the earlier holes first.** The gaps recorded in section 0.1, meaning the section 2.2 boundary mitigations, seed control, artifacts B through D, and the five unmeasured arena numbers, come before any distribution work at all. Preparation for this section, meaning packing, a release workflow, and `docs/`, starts once those are closed.
2. **Prepare, but do not publish, until tier 3 works and has worked examples.** Publication is the one step in this plan that cannot be undone: mod internal names are first come, and a NuGet ID can be unlisted but never deleted (risk 1). A framework that cannot test netcode is not finished enough to ask anyone to commit those names to. The bar is tier 3 running *and* worked examples against a real mod, of the kind section 8.3b produced for tiers 1 and 2 against ExampleMod. Until then every artifact stays a local build or a draft release.
3. **CI for the game tiers lands in the same change as the first GitHub publication, not after it.** The recipe in section 8.3a is what makes a release trustworthy rather than a zip that one machine built once. Publishing a `.tmod` that no machine but a developer's own has ever run would defeat the point of a testing framework, so the job that builds tModLoader and runs `scripts/run-all.sh` is part of the release change rather than a follow-up to it.
4. **nuget.org comes after GitHub, never before.** GitHub Releases carry artifact A and prove the pipeline end to end on a channel where a mistake is recoverable. The NuGet packages, B through E, follow once that channel is working, because the ID commitment is permanent and the audience is wider.

## 6. Versioning

Three clocks, kept deliberately separate:

- **NuGet packages:** full SemVer 2.0, prerelease tags allowed, normal .NET expectations.
- **The `.tmod`:** `build.txt` `version` is 2 to 4 integers and cannot carry a prerelease tag. Map `1.2.3` to `1.2.3` and use the fourth component for prerelease iterations (`1.2.3.1`), keeping the real SemVer string in the git tag and the release notes.
- **tModLoader compatibility: 1.4.5 only.** Settled. The 1.4.4 and 1.4.5 target frameworks are incompatible (section 1.1.14), and supporting both would mean two branches, two CI matrices, and two sets of release artifacts. Building against the line the framework will live on avoids paying a porting cost twice. If 1.4.4 support is ever required, mirror tModLoader's own branch-per-target structure rather than trying to multi-target a single project.

`modReferences = Testaria@1.2` is the only compatibility enforcement the Workshop offers, so keep the minimum-version discipline tight and treat any change to the attribute or assertion surface as a minor bump at minimum.

## 7. Risks and open decisions

1. **Naming is the only irreversible decision here.** Mod internal names are globally first come, and renaming a published mod orphans its subscribers. NuGet IDs can be unlisted but never deleted. Settle the brand before publishing anything to any channel.
2. **False confidence at the Tier 0 boundary** (section 2.2). This is the highest-severity design risk, because the failure mode is a green test suite that proves nothing.
3. **World state isolation between Tier 2 tests** is the hardest engineering problem, and section 2.4 sets out a four-mechanism working design that was explicitly provisional pending real test surface. Five numbers in it were unverified: the `Main.SceneMetrics` scan radius, the default banded box size, the quarantine duration, the split between the banded and column arena regions, and the width at which a spanning column stops being cheaper than `[FreshWorld]`. **All five are measured** (sections 8.5d and 8.5f), two of them setting the defaults the arena ships with and one of them producing the answer "this question does not arise".
4. **Reload safety.** Every hook, event subscription, and static registration the framework makes must be undone in `Unload()`, or it pins dead `AssemblyLoadContext` instances (`AssemblyManager.cs:177`, `196`). A test framework that leaks across reloads will be blamed for the leaks of the mods it tests.
5. **Nondeterminism.** Seed control over `Main.rand` and `WorldGen.genRand` must be a day-one feature. **Built** (section 8.5b). There is one generator rather than two: on the 1.4.5 line `WorldGen.genRand` is a property returning `Main.rand`.
6. **The 1.4.5 toolchain, settled by opting into the beta branch.** Everything above tier 0 builds and runs against a `1.4.5-dev` Steam install. The reasoning is worth keeping because CI has to solve the same problem without Steam (section 8.3a), and because the naming trap at the end of this item still catches people. The Steam release of tModLoader is still the 1.4.4 line (`net8.0`, `LangVersion 12.0`) even though Terraria 1.4.5.8 has shipped, and no GitHub release carries a 1.4.5 asset since the release tags all come off the 1.4.4 branch. Reaching 1.4.5 means either opting into the `1.4.5-dev` Steam beta branch (password `iamacontributor`) or running `setup-cli.sh` to decompile and patch from source, which also requires a Terraria install and generates the `src/` tree the checkout currently lacks. Note the naming trap: the `preview-*` Steam branches are the monthly CI channel on the **1.4.4** line, not 1.4.5, and installing one yields `net8.0` with `LangVersion 12.0`. This does not affect `Testaria.Core`, which references neither, but it gates every tier above 0.
7. **tModLoader is a moving target.** The 1.4.5 port is in progress; `MigrationGuide_1.4.5.md` and `PortingNotes_1.4.5.md` are live documents. Expect churn in whatever hooks the framework attaches to, and expect to run `tModPorter` more than once. **This is not theoretical**: a tModLoader update renamed `ProjectileID.Sets.PlayerHurtDamageIgnoresDifficultyScaling` to `SelfHurtPlayers`, which stops an already built ExampleMod from loading at all (section 8.6b). Section 8.6e measures the same churn across the ecosystem: of six published mods with active 1.4.5 branches, four did not compile against the current dev build, and two of those referenced `FocusHelper` members removed in the 1.4.5.8 update, one of them on a branch committed to a week after that update landed.
8. **Upstreaming.** The TML team currently has only hand-driven failure-case mods in `test/Test Local/`. If this framework works, they may want it in-tree. Keeping it MIT and structurally separable from any one mod preserves that option at no cost.
9. **Test mods under `ModSources`.** `ModSources` lives under the shared stable save path, so all build purposes (Stable, Preview, Dev) share it. A test mod placed there is visible to every tModLoader install on the machine, which is convenient for iteration and surprising if unexpected.

## 8. Milestones

### 8.1 Close the loop before adding anything

Deliberately Tier 1 rather than Tier 0, since a Tier 0 suite already works with a stock test project and proves nothing section 1.1.5 does not. The gap this milestone closes is not features: the core is heavily self-tested and the game layer is compile-verified only, so **nothing runs inside the game** until this is done. It exercises the spine rather than lengthening it.

1. Package `Testaria` as an actual `.tmod`.
2. A throwaway self-test mod: one Tier 1 test, one Tier 2 boxed test.
3. The harness: provision a scratch save directory via `-tmlsavedirectory`, drop in the `.tmod` files, write `enabled.json`, launch `-server` with `-autocreate`, pipe `testaria run`, map the result to an exit code.
4. Run it green.

### 8.1a Making an empty dedicated server tick

An empty `-server` loads mods and accepts console commands but never simulates: measured, the world clock read the same time eight seconds apart and the process accrued about a second of CPU per twenty of wall clock. It is headless, but it is also paused, so a harness has to make it tick.

The chain:

- `Main.DedServ_PostModLoad` holds the server loop, `IL_0DFF` to `IL_0EAC`. At `IL_0E29` it reads `Netplay.HasFullyConnectedClients`. True calls `Game.Update` and branches past everything else; false invokes `Main.OnTickForThirdPartySoftwareOnly`, then `Netplay.UpdateInMainThread`.
- `Main.Update` calls `DoUpdate`, which reaches `DoUpdateInWorld` only if `Main.ShouldUpdateEntities()` (needs `_worldPreparationState == Ready` and `!WorldGen.generatingWorld`) and `Terraria.Testing.WorldUpdateStepper.ShouldUpdateWorld()` (true unless paused). `DoUpdateInWorld_Inner` then calls `SystemLoader.PostUpdateEverything`.
- Both of those secondary gates measured open on a server, so `HasFullyConnectedClients` is the only one that matters.

**The flag has to be held open from the thread that writes it.** Its sole writer, `Netplay.UpdateConnectedClients`, runs from `Netplay.ServerLoop` on a **separate network thread**, continuously. Setting it from the main thread is a race against that thread, and the main thread loses essentially always: winning once buys exactly one tick, which is why the idle branch's `Netplay.UpdateInMainThread` is the wrong place to stand even though it fires every iteration.

**So the hook goes on the writer, on the writer's own thread.** A `MonoModHooks` detour on the private `Netplay.UpdateConnectedClients`, setting the flag after the original runs, holds it open. The log confirms the placement: the hook reports from `Server Loop Thread` rather than `Main Thread`.

Scoped to dedicated servers, and only while a run is in progress, so a normal server is untouched.

**Result: all five self-tests pass in a live headless server**, with boxes leased at x=8, 72 and 136 in the Cavern band. That spacing is 64 tiles, exactly the 48-tile width class plus two 8-tile gutters, which is the arena's geometry confirmed against a real world rather than a unit test.

### 8.1c A blank world, and reserved ground

Most of what can intrude into a box is not an entity: liquid flows, sand falls, grass and corruption spread. An ownership warden cannot see any of that, and every one of them is a race. Generating a world that contains none of it removes the whole class at the root, which is a stronger guarantee than any amount of watching.

**Measured before building anything.** The `skyblock` secret seed, present in 1.4.5 as `WorldSeedOption_Skyblock` with a `ServerConfigName` of `"skyblock"` and therefore usable straight from the server command line, produces a world ready in **8 seconds against 20 for an ordinary small world**, with the self-tests still passing.

Generation is 20 seconds of the cycle rather than the bulk of it, so eight seconds against twenty is worth having and is not a transformation. **The case for a blank world is contamination and determinism, not speed.**

`skyblock` is not the answer regardless, because it sets `Main.skyblockWorld`, and a secret seed is a different game rather than different terrain. It is read in seven places, including `Liquid.QuickWater` and `Player.BordersMovement`. A test that passes under it and fails in an ordinary world is exactly the false signal this design keeps trying to eliminate.

**The answer is `[BlankWorld]`, built on `ModSystem.ModifyWorldGenTasks(List<GenPass>)`,** which lets a mod delete generation passes outright. One hard constraint: `TerrainPass.ApplyPass` is what assigns `Main.worldSurface` and `Main.rockLayer`, on which `WorldGeometry` and the whole arena depend. Either that pass survives, or the generator sets `GenVars.mainWorldSurface` and `mainRockLayer` itself; both have setters. Every `Main.*World` flag stays false, so the rules are vanilla's.

Blank means a known uniform substrate, not literal emptiness: solid stone below the surface, air above, no liquids, no ore, no structures, no chests, no corruption seeds. Pure air would give entities no floor and make NPC behaviour tests meaningless.

#### Reserved ground

A blank world must still be a *valid* world: vanilla assumes a spawn point exists, and a dungeon, and so on. Those go in **named reserved areas that the arena never leases**, rather than being scattered through the test ground.

A test whose subject really is the dungeon leases it by name through `Arena.TryLeaseReserved`, which is deliberately a separate call from `TryLease`. That makes "this test depends on vanilla furniture" a declaration the test makes out loud, rather than an accident of where a box happened to land.

Ordinary leases step *over* reserved ground rather than being refused by it, since the arena has room to spare and a test should not lose a box merely because the dungeon sits to its left. Reservation is a property of how the world was built, so the arena reserves nothing unless the generator tells it what it placed and where.

### 8.2 Red propagates, and stays distinguishable

The step most easily skipped, and the one that matters most: a framework that can only report green is indistinguishable from one that works.

`TestariaRedTest` is a mod of four deliberately broken tests, enabled only by `scripts/check-red.sh` and never by an ordinary run. The check asserts not merely that the harness exits non-zero, but that each problem arrives as the *right kind*, with a non-empty message. A failed assertion and a broken test must stay distinguishable all the way out, or a report cannot tell an author whether their mod or their test is at fault.

Measured, all four survive the trip from assertion through the coroutine and the runner into JUnit XML and out as a non-zero exit code:

| Deliberate problem | Reported as | Message |
| --- | --- | --- |
| Failed assertion | failure | "deliberate assertion failure" |
| Exceeded tick budget | failure | "exceeded its budget of 30 ticks while blocked on `Wait.Ticks(1)`" |
| Thrown exception | error | "InvalidOperationException: deliberate error, not an assertion" |
| Malformed test method | error | "must return void or IEnumerator, but this returns Int32" |

The timeout naming what the body was blocked on is the part worth keeping. A bare "timed out" is close to useless when debugging; knowing it was stuck on a particular wait is most of the diagnosis.

### 8.3 Calibrated against ExampleMod

The first real-mod test is a measurement instrument, and an instrument is calibrated against a known standard before it measures an unknown. ExampleMod is that standard: not because it is small, at 563 files it is not, but because it is broad and shallow, every piece a minimal and deliberately documented demonstration, and because the tModLoader team maintains it, so a test that cannot be made to work is a framework bug rather than somebody's mod quirk.

It builds against the installed 1.4.5-dev with zero errors once given a `.csproj` that imports the *installed* `tMLMod.targets` rather than the repository's, which expects the decompiled `src/` tree that only `setup-cli.sh` produces. `scripts/build-examplemod.sh` does that in a scratch copy and never touches the checkout.

Content is resolved by name through `ModContent.TryFind` with a weak reference, which is how cross-mod code is normally written and which lets the test mod load and report honestly when its subject is absent.

**Five tests, all passing**: the mod is loaded; its item resolves by name with an id above the vanilla range; its item has a localized display name, which only a completed load pass provides; its critter resolves; and its critter can be spawned into a box and survives a second of its own AI.

#### What the calibration settled

Neither shows up until a real mod is the subject, which is the whole argument for calibrating before writing anything else.

**A test can skip itself at run time.** Whether an optional mod is installed, or a world has the right biome, is only knowable once the game runs, so a compile-time attribute cannot express it. Failing would blame a subject that is not broken; passing vacuously is worse, claiming coverage that never happened. `Assert.Skip` and `SkipTestException` report it honestly.

**Spawned entities need an owner, or the game reclaims them.** `NPC.CheckActive` reclaims anything outside `activeRangeX/Y` of a player, and a headless server has no players, so *everything* is out of range. Measured directly: ExampleMod's critter is alive on the next tick and gone a second later. That is the game behaving correctly, so the answer belongs in the framework: a `GlobalNPC.CheckActive` returning false for entities a live test owns. It is also the first working piece of the ownership warden from section 2.4.

### 8.3a Continuous integration for the game tiers

Their `build.yml` runs on pushes and pull requests to the `1.4.5` branch, with `TERRARIA_VERSION: 1458`. It obtains Terraria like this:

```
curl -s -L https://terraria.org/api/download/pc-dedicated-server/terraria-server-1458.zip
```

A public, unauthenticated download from Re-Logic. No Steam anywhere. It then runs `setup-cli decompile --key $TERRARIA_OWNERSHIP_KEY` and caches the decompiled tree, encrypted with a passphrase, against a key of `1458-A`.

The only real gate is that ownership key, documented in `DecompileCommand.cs:40` as "Terraria ownership key in hexadecimal format... usually derived from the installed Terraria.exe", and derivable with the setup tool's own `ownership` command. So the recipe for our own CI is:

1. Derive the key once, locally, from an owned Terraria install. **It is a secret and must never be committed**; it belongs in repository secrets.
2. Download the public server zip.
3. Decompile and build tModLoader, caching the result as their CI does, since decompilation is far too slow to repeat per run.
4. Build the mods and run `scripts/run-all.sh`.

That is real work, and the cache is what makes it viable rather than absurd, but it is not blocked.

**Scheduled:** section 8.7 ships this recipe in the same change as the first GitHub release, for the reason given in section 5.1. Until then CI covers the core tiers only.

#### tModLoader's own CI does not run its own tests

Worth recording, because it says something about the gap this framework fills. Across all five workflows on the 1.4.5 branch there is not one occurrence of `dotnet test`, `vstest`, or a reference to `tModLoaderTests`. Their CI builds 1.4.5 and publishes it; the MSTest project in `test/` is never executed by it.

So the ecosystem's central project has a test suite that CI does not run, and no way at all to test a mod's behaviour in a running game. That is the hole, and it is larger than "mods lack a test framework".

### 8.3b A real suite for ExampleMod

Thirty-nine tests across registration, localization, items, NPCs, tiles and recipes, all passing. Broad rather than deep on purpose: assertions are written as invariants over whole content sets rather than claims about particular items, so they keep their meaning as the subject changes and say something about the mod rather than about one line of it.

Two things about the framework only a suite this shape can settle.

**Parameterised tests are what a suite of invariants needs.** A loop over a content set inside one test reports a single result, so one failure hides the rest and the report names the test rather than the case. `[Case]` and `[CaseSource]` expand each case into its own result, which takes this suite from 39 tests to 924, each named and filterable individually. See section 8.3c.

**Three obvious assertions are wrong about Terraria rather than about ExampleMod**, which is exactly what calibrating against a known-good subject is for:

- Damage of `-1` is vanilla's sentinel for "not a weapon", used by plenty of accessories. Asserting `damage >= 0` fails on `RubyEarrings`, which is fine.
- Not every NPC stands alone. Worm segments despawn at once without a head, correctly. A blanket "every NPC survives spawning" test is wrong; spawning is testable, surviving is the NPC's own business.
- A recipe consuming its own output is legal and sometimes deliberate, so the test names such recipes rather than forbidding them.

Each is the test being wrong, not the subject. A framework calibrated against something that can be assumed correct turns that ambiguity into information.

### 8.3c Parameterised tests

A test with parameters and a source of values runs once per case. There is deliberately no separate `[Theory]` marker: the tier attribute already marks a method as a test, so having data parameters is what makes it parameterised, and a leading `ITestContext` is the context rather than data.

`[CaseSource]` is the reason it earns its place. Cases that only exist once the game has loaded cannot be written out by hand, and discovery runs in the game for every tier above zero, so a source can enumerate them. The ExampleMod suite expands from 39 tests to **924**, each named by its arguments and filterable individually: `Has_a_sensible_value_and_damage("RubyEarrings")` can be run on its own out of all 924.

Three decisions worth keeping.

**Named `Case` and `CaseSource`, not `InlineData` and `MemberData`.** Mirroring xUnit's vocabulary is the usual rule here, but tier 0 projects use xUnit *and* `Testaria.Core` together by design, and same-named types in both make `using Xunit; using Testaria;` ambiguous, so the test project does not compile.

**Cases are named by their arguments rather than numbered.** Numbering would make a report say which case failed only in the sense that a line number does. Naming is also what lets a filter address one case.

**An empty source reports a skip, not an error and not silence.** A source can legitimately be empty, when the content it enumerates is not installed. Reporting nothing would leave the suite looking complete; reporting a failure would blame nobody in particular.

One case the error path has to handle: **a `string` is `IEnumerable`**, so an unguarded source check turns a string into one case per letter instead of reporting it.

### 8.3d A player, and the tests it makes possible

A great deal of Terraria does nothing without a player: NPCs target a player, biomes are measured from one, spawning and despawning are decided by distance to one. Without one, an entire category of test is unwritable, arguably a larger gap than Tier 3.

`TestContext.SpawnPlayer` fabricates one rather than connecting a client. That is exactly what the game does for a joining client, `Main.player[i] = new Player()`, and vanilla keeps a dummy of its own for scene metrics, so the shape is unremarkable. Nothing is networked, drawn, or given input. Slot 255 is avoided, since `Main.myPlayer` is 255 on a server and taking it would make the server think it is its own client.

Players are replaced rather than merely deactivated at teardown, so no state survives into a slot's next occupant, and a test asserts that no player outlives the test that made it.

**Natural spawning is suppressed alongside it.** It is driven entirely by proximity to a player, which is why it does not happen on a server with none; the moment a test puts a player in its box, the game starts populating the area around it, and those arrivals are contamination by any definition. `GlobalNPC.EditSpawnRate` is the sanctioned lever, and is preferable to `NPC.noSpawnCycle`, which is private and resets itself after a single call. Better not to create the intruders than to detect them.

A test spawns a player and a zombie and asserts the zombie targets the player, which has no subject at all without one.

### 8.3e How fast a run ticks, and stopping it

The dedicated server paces itself to real time, so a tick is 16.7 ms of wall clock and a thousand-tick test takes 16.7 seconds on any machine. Section 2.4 records that measurement; this is the mechanism that answers it.

The rate is not configurable by any sanctioned means. The server build's `Main` derives from a stub `Terraria.Server.Game` whose `TargetElapsedTime` returns `TimeSpan.Zero` and discards writes, so the XNA knobs do nothing, and the loop's period is a bare local (`double num6 = 16.666666666666668` in vanilla, `double delta = 1000 / 60D` in tModLoader's rewrite). Neither is a field, a property, a config value, or a parameter, and a mod cannot patch the method either: mods load from inside `DedServ`, so the frame that will run the loop is already on the stack before any hook can exist.

What is reachable is the `Thread.Sleep` that fills out each tick. Shortening it is the whole mechanism. `RunPacing` and `TickRateGovernor` expose it as `-testariaspeed` and `testaria speed`, either a rate in ticks per second or `max`. **The simulation is unchanged**: one update per iteration, same hooks, same order, same ratios, only the wall clock differs, confirmed by the self-test suite consuming the same 452 ticks and reporting identical per-test tick counts at every speed. Measured, the suite runs 7.08s at realtime and 0.91s at `max`.

Two consequences worth keeping. A bounded rate is reproducible across machines and `max` is not, so CI should name a number rather than take the last few seconds. And anything keyed to the wall clock rather than to ticks, a real elapsed duration, a background `Task`, a timer, cannot survive fast forward, so `[RealTime]` opts a test back down to 60 tps and the run's own speed resumes afterward.

Stopping the world came with it. `ITestContext.Pause`, `Step(n)`, and `Resume`, plus `[StartPaused]`, let a test walk the simulation a tick at a time, which is how you pin down the exact tick something goes wrong on. It reuses the game's own debug gate: `DoUpdate` keeps running and only `DoUpdateInWorld` is skipped, so a frozen tick is a shape the game already produces. A test that pauses and neither steps nor resumes is thawed by the harness after 1800 frames with a note on its result, so a wedged test cannot hang a run.

### 8.4 The arena under load

Calamity-class mods stress precisely what section 2.4 defers: entity pool exhaustion, mod-count interactions, world generation at scale, long reload times. That deserves to be a named milestone aimed at the **arena**, run deliberately, rather than something stumbled into while trying to test gameplay.

#### There is no Calamity-class mod for 1.4.5, measured rather than assumed

Calamity is installed, in four Workshop builds. The newest, 2.2.2, is a 1.4.4 build, and on this 1.4.5 install it fails at load with `Method 'Place' in type 'CalamityMod.World.Planets.Planetoid' does not have an implementation`, which is what section 1.1.14's hard fork looks like from the inside. Nothing of that size has been ported yet.

**The attempt exposes a reporting hole.** A run whose mod fails to load reports on a game nobody asked for: three mods requested, two run, *six of six tests passing*, and nothing saying so. The tool compares the mods it enabled against the list the game prints at run start, and fails with exit 2 naming the missing ones. It is the same shape as the stale package in 8.5a and the silent skip in 8.6b: **the run reports on a game that is not the one anybody asked for.**

#### So the stresses were applied directly

`TestariaLoadTest`, run by `scripts/check-load.sh` and by `RUN_LOAD=1 scripts/run-all.sh`, applies what a huge mod would apply, more precisely than a huge mod would:

- **Churn**: 300 boxed tests in a row, leasing and returning, exercising the free list, carving and quarantine.
- **Every size class**, including the 384 by 256 largest and the spanning columns nothing else asks for. Measured across a corpus of 967 in section 8.5f: zero spanning requests, so the column path runs nowhere but in unit tests.
- **Entity pool pressure**: 260 spawns into a 200 slot pool, and a following test asserting the next box is clean.

Result: 308 boxes leased and returned in about 40 seconds of wall clock, covering 48x32, 96x64, 192x128, 384x256, and columns of 48x879 and 96x879. The slowest restore was 21.5 ms, for the 384 by 256 box. No box took more than two ticks to go quiet. Per-tile snapshot and restore cost held at 0.25 microseconds across a sixty-fold range of box sizes, which retires the worry in section 2.4 that recycling a column would be prohibitively expensive: the largest column in a small world costs about 20 ms.

Spawning past the pool behaves: the spawns start coming back inactive rather than wrapping onto somebody else's slot, and the next box is clean.

#### A box taller than the band it names

**An impossible box request hangs an unguarded run.** `Arena.Carve` throws an `ArgumentOutOfRangeException` that says exactly what is wrong and what to do about it, and **tModLoader silently catches exceptions in its hooks**, so an exception escaping there completes no test, advances no session, and leaves the run to sit until the harness times out: the clearest error message in the framework, inside an exception nobody ever sees.

The runner turns a failed lease into an errored result carrying the arena's own message. There is a unit test for it, and the request lives in `TestariaRedTest` where the red gate checks it arrives as an error rather than as a hang.

The general rule matters more than the one case: **an exception is not a report.** Anywhere this framework throws inside a game hook, it is one silent catch away from hanging a run, and only running it finds those.

#### Mod count and load time

Measured on this install: two mods load in 867 ms; five, including ExampleMod's 563 files and 6 MB, in 1570 ms. So ExampleMod costs about 700 ms of load on its own, and a run's fixed cost is dominated by world generation (5 seconds) and mod loading rather than by tests, which averaged 130 ms each in the churn.

Calamity is roughly an order of magnitude larger again than ExampleMod, so the plan's worry about long reload times is real but is a property of tModLoader rather than of the arena, and it will have to be measured again when a mod that size exists for 1.4.5.

### 8.5 Fill the holes before building anything outward facing

Everything in section 0.1's table that is not a tier. These come first because each one is a correctness problem in what already exists, and shipping over them would make them permanent:

1. ~~**The section 2.2 boundary mitigations.**~~ Done, section 8.5a. The analyzer over `ContentSamples`, `ModLoader.Mods`, `Lang.*`, `ModContent.*`, and `*ID.Sets.*`, the `[RequiresLoadedGame]` marker, and the runtime guard behind it.
2. ~~**Seed control (risk 5).**~~ Done, section 8.5b. Pinned per test, derived from the test's identity, and recorded in the report.
3. ~~**Artifacts B, C, and D (section 4.2).**~~ All three are done: D in section 8.5c, B and C in section 8.5e. Every artifact in the matrix now exists.
4. **The five unmeasured numbers (risk 3).** The `SceneMetrics` scan radius is measured (section 8.5d). The remaining four, the default banded box size, the quarantine duration, the banded and column region split, and the `[FreshWorld]` crossover width, all want a real test corpus to calibrate against rather than another reading of the source.

### 8.5a The Tier 0 boundary, enforced rather than documented

Section 2.2's three mitigations, all of them, since the first one alone only protects projects that never need a tModLoader reference.

**Mitigation 1 is `Testaria.Core` itself**: it references nothing from the game, so the dangerous surface is not in scope for a project that references only the core. The templates are built that way.

**Mitigation 2 is `TSTA001`**, a Roslyn analyzer over exactly the list the measurements name: `ContentSamples`, `ModContent`, `Lang`, `Terraria.Localization.Language`, the load-reporting members of `ModLoader`, and any member of a `*ID.Sets` type. `Main` is deliberately absent, because it throws a `TypeInitializationException` and is therefore already self-correcting. Consts on the `*ID` types are equally deliberately absent: they are inlined at compile time and stay true without a game, and a rule that fires on `ItemID.Count` would be a rule people switch off.

Three decisions worth keeping.

**It ships inside the `Testaria.Core` package rather than waiting for artifact B.** A tier 0 project already references the core, so pairing them means nobody can take the helpers without the guard.

**A mod assembly is exempt automatically**, detected by the compilation declaring a type that implements `ILoadable`. This matters less than it appears, since an analyzer arrives through NuGet and a mod build cannot consume NuGet (section 1.1.1), but a hand-imported analyzer in a test mod would otherwise flag code that is provably loaded.

**`TSTA002` makes a declared dependency travel.** Marking a member `[RequiresLoadedGame]` silences `TSTA001` inside it, and would be a pure silencer if that were all it did; instead the requirement propagates to undeclared callers, so it walks up the call graph to the test that has to answer for it rather than stopping at whoever wrote the helper.

**Mitigation 3 is the runtime half**, `GameState.Require`, for what an analyzer cannot see: reflection, a cross-mod `Mod.Call`, or a type the list has never heard of. It throws a `LoaderStateException`, its own type so that "this test is in the wrong tier" stays distinguishable from "the code under test is broken". The flag it reads is raised in the Testaria mod's `PostSetupContent`, which is the first moment the claim is actually true, and lowered in `Unload`, because a flag left raised across a reload would vouch for a load context being torn down (risk 4).

**Measured rather than assumed, twice.** The analyzer's own suite runs it against a stub of the Terraria surface, 14 cases split between what must be flagged and what must not. The packaged analyzer is then built into a scratch project referencing the *real* `tModLoader.dll`: four errors on the four loader-dependent lines, nothing on `ItemID.Count` or `new Item().damage`. The second check is the one that proves the package rather than the code, and it has to clear the global NuGet cache to run at all, since a cached `Testaria.Core` shadows a local feed and a project can restore an older package while every unit test passes.

In the game, one self-test asserts `GameState.IsLoaded` is true, which is the only place that direction can be asserted, since it is the mod's own load pass that raises it.

### 8.5b Seeded per test, by identity rather than by order

Risk 5, and the reason it was worth doing before a real suite exists rather than after: retrofitting determinism onto tests written without it means rewriting the tests, not just the framework.

**One generator, not two.** The plan said `Main.rand` and `WorldGen.genRand`. On the 1.4.5 line `WorldGen.genRand` is a property returning `Main.rand`, confirmed in the decompiled 1.4.5.8 source and again in tModLoader's own `WorldGen.cs.patch`. They were separate fields on 1.4.4, so this is exactly the kind of difference that would silently halve a fix ported between branches. A self-test asserts the two are the same instance, so a version that separates them again says so rather than quietly leaving world generation unseeded.

**Seeded in place, not by swapping the instance.** `UnifiedRandom.SetSeed` is public, and setting the seed on the generator the game already holds covers anything that cached a reference to it, which swapping `Main.rand` would not. The cost is that there is no old stream position to restore, so teardown reseeds from the clock instead. Nothing depends on the game's randomness resuming where it left off, and the alternative leaves cached references unseeded, which is the failure that would be hardest to notice.

**A test's seed comes from its own identity, never from a counter.** `TestSeed.For(runSeed, className, name)` is FNV-1a over the test's full name mixed with the run seed. A counter would have made a test's seed depend on how many tests ran before it, so filtering a suite down to the one failing test would hand it a different seed and quite possibly a pass, which is precisely when reproducibility is worth the most. Hand-rolled rather than `string.GetHashCode`, which is randomized per process by design and would have made seeds differ between two runs of the same suite on one machine.

Measured end to end: the same test reports seed 991526881 in the full 36 test run and in a filtered single test run, and `RUN_SEED=7` moves it to 991519236.

**`[Seed(n)]` pins a particular roll**, for the seed that reproduced a bug or one chosen to make a rare branch happen. It ignores the run seed, since a seed that only reproduces the bug at one run seed is not what the author asked for.

**What seeding can and cannot promise.** A Tier 1 body runs synchronously the moment its test begins, so its first draw genuinely is the first draw after the reseed, which is what lets the self-tests assert exact values. A Tier 2 body resumes a tick later, by which point the world has drawn from the same generator on its own account, so Tier 2 reproducibility is "given the same world and the same ticks" rather than absolute. Worth stating plainly in the documentation rather than letting an author discover it from a flaky test.

### 8.5c Artifact D, the `testaria` command

The plan calls A and D the minimum viable pair. The shell scripts do D's job from a checkout of this repository and on a machine with bash, and nowhere else, which makes the honest answer to "how do I run my mod's tests?" into "clone Testaria first".

`testaria run --mod MyModTests --blank --speed max` now provisions a scratch save directory, installs and enables the named mods, generates a world, starts a headless server, sends the console command, waits for the report, stops the server, and turns the result into an exit code. `testaria list` catalogues without running.

Decisions worth recording.

**A pipe rather than a FIFO.** The shell harness feeds the server's console through a named pipe, which is the part of it that is least portable. A redirected standard input does the same job through `Process`, and works on Windows, which is most of the argument for having the tool at all.

**No Xvfb.** The scripts prefer a virtual framebuffer and fall back to `SDL_VIDEODRIVER=dummy`; the tool only ever uses the dummy driver, and the full self-test suite passes under it. One less thing to install, and one less Linux-shaped assumption.

**The runtime mod is enabled whether or not it was named.** A run without `Testaria` loaded has nothing to run the tests, and would sit there until the timeout with no explanation of why.

**Three exit codes, not two.** 0 for a clean run, 1 for a test that failed, errored, or was blocked, and 2 for a harness that could not run the tests at all. A build should be able to tell "your mod is broken" from "the game would not start", and collapsing those is how a flaky environment gets mistaken for a flaky suite.

**Blocked counts as red**, as it does in the runner. A test that never ran has established nothing about its subject.

Verified by packing the tool, installing it with `dotnet tool install` into a directory with no relationship to this repository, and running a real suite: four tests, green, exit 0. And by the red path: the deliberately broken mod reports two failures and three errors, and exits 1.

The scripts stay as they are. They are this repository's own gates, they do things the tool has no business doing (building ExampleMod in a scratch copy, running the red check, giving each `[FreshWorld]` test a process), and having both means the tool's behaviour is checked against something rather than only against itself.

### 8.5d The biome scan, and why the gutter stays at 8

The first of risk 3's five numbers, and the one section 2.4 could not read from a checkout: `SceneMetrics.ScanAndExportToMain` is unpatched vanilla, so it lives in the generated `src/` tree that a fresh tModLoader checkout does not contain. It is perfectly readable in a decompile, which section 1.4 establishes as the faster way to read the game. From `Terraria/SceneMetrics.cs` in 1.4.5.8:

```csharp
private static readonly Point AssumedConstantScreenSize = new Point(1920, 1200);
private static readonly int ZoneScanPadding = 25;
public static readonly Point ZoneScanSize = new Point(
    AssumedConstantScreenSize.X / 16 + ZoneScanPadding * 2 - 1,
    AssumedConstantScreenSize.Y / 16 + ZoneScanPadding * 2 - 1);
```

`ScanTiles` scans `Utils.CenteredRectangle(TileCenter, ZoneScanSize)`, centred on the player's own tile. So the rectangle is **169 by 124 tiles**, reaching **84 tiles sideways and 62 up and down**. Against a gutter of 8, that is not a tuning discrepancy, it is an order of magnitude, and it settles the question section 2.4 left open: **geometry alone cannot biome-isolate a box.** Eight tiles of dead space covers tile framing and liquid, and nothing about biomes.

Two things keep that from being an emergency.

**A biome needs three hundred tiles of one kind before it counts** (`CorruptionTileThreshold` and its neighbours), so a stray block does nothing and a test has to mean it. What has no threshold is the singular scenery, a campfire, a heart lantern, a water candle, a music box, each of which counts from one.

**Boxes run one at a time.** A released box has its tiles restored and sits in quarantine before the next tenant arrives, so there is nothing left of the last test to reach anybody. The reach only binds when two boxes are live at once, which is exactly the parallelism section 2.5 defers.

So the number is recorded in `BiomeScan`, with the derivation and the citation, and the gutter stays at 8 as a deliberate floor rather than an unexamined guess. Paying eighty-four tiles of dead space on every side of every box buys nothing today. The decision belongs with the parallel scheduler, which is the change that makes it matter, and the measurement is recorded for it.

The remaining four numbers, the default banded box size, the quarantine duration, the region split, and the `[FreshWorld]` crossover, are not readable from any source file. They want a real corpus to calibrate against, which is section 8.6's business.

### 8.5e Artifacts B and C, and a gate that consumes them

`Testaria.Unit` is the `BuildMod=false` wiring from section 1.1.5, packaged: references an install's assemblies without packaging a `.tmod`, sets the `LangVersion` that `tMLMod.targets` only sets while building a mod, carries `Testaria.Core` and the boundary analyzer with it, and errors with `TSTU001` when there is no install rather than emitting a hundred missing-type errors. Deliberately the second choice: a test project that never references the game cannot accidentally depend on a loaded one, which is mitigation 1 and still the strongest.

`Testaria.Sdk` is one MSBuild target. `dotnet build -t:TestariaRun` provisions, runs, writes the report to `TestResults/`, and fails the build when the suite fails, which is the whole reason to run tests from a build. It ships the CLI inside itself under `tools/`, so a mod repository needs one `PackageReference` and no install step, and the tool cannot drift from the targets that invoke it.

Worth recording: **a mod project can consume a build-only package**, even though section 1.1.1 says mods cannot consume NuGet. The rule is about *assemblies*: `ModCompile` packs only `dllReferences`, so a package that contributes a library leaves a mod broken at run time. A package that contributes nothing but MSBuild has no assembly to lose, so `Testaria.Sdk` works in a mod project as well as beside one.

#### The gate that consumes them

`scripts/check-packages.sh` packs all four packages into a temporary feed and consumes them as a stranger would: the analyzer must fire from `Testaria.Core`, `Testaria.Unit` must report a missing install and must put the game in scope when there is one, and `Testaria.Sdk` must run a real suite green from MSBuild and leave the report where it said it would.

Three things it checks that no unit test can see, each of them a way a well-formed package fails a project that references it.

1. An **XML comment containing `--mod`** makes MSBuild refuse to load the file at all, while `dotnet pack` considers the package well formed.
2. **`TestariaHasTml` belongs in the targets, not the props.** A package's props are imported *above* the project body, so a project setting `tModLoaderSteamPath` for itself would be overridden by a decision made before it spoke. The targets are below the body.
3. **`TestariaResults` belongs there too**, for the same reason with a quieter symptom: derived above the body it takes the project's default name rather than the `TestariaRunName` the project chose, and a CI job collecting `TestResults/MyMod.xml` finds nothing and reports no tests rather than a failure.

The pattern behind all three is worth keeping: **the packages are the product, and the only way to test a product is to consume it.** The gate clears the NuGet cache itself, so it cannot answer from yesterday's build.

### 8.5f The arena's remaining four numbers

Risk 3's other four numbers are properties of a test suite rather than of Terraria, so they could not be read out of a decompile the way the biome scan was. They needed a corpus, and there is now one: 967 tests, 33 of which lease a box.

`ArenaMetrics`, behind `--measure`, writes a row per boxed test: the size granted, the patch of ground actually changed, how far the test's own entities ranged, what snapshot and restore cost, and how many ticks the box took to go quiet after teardown.

#### What it found

| Number | Was | Measured | Now |
| --- | --- | --- | --- |
| Default banded box | 80 by 48 | Furthest any test's entities ranged: 25 by 21. Largest patch of ground changed: 5 by 3, by 7 tests of 33 | 48 by 32 |
| Quarantine | 60 ticks | 31 of 33 boxes quiet the tick after teardown; the slowest took 2 | 12 ticks |
| `[FreshWorld]` crossover width | Unknown | Snapshot plus restore costs 0.35 microseconds per tile, so a full-height column spanning an entire small world costs 1.8s against 5.5s to generate a blank one | No crossover exists |
| Banded and column region split | 0.25 | Not one test of 967 asked for a column | Unchanged, and recorded as uncalibrated |

#### Three of those deserve more than a row

**Ground changes are the wrong measure of box size.** Recording only tiles that differ from the snapshot reports that most tests change nothing at all. True, and useless: an NPC left to its own devices covers ground without touching any of it, and it is the roaming that decides how big a box has to be. Measuring where a test's own entities actually go gives 25 by 21 as the worst case, which is what 48 by 32 is chosen to cover.

**The crossover question dissolves rather than resolving.** A column costs about 0.35 microseconds per tile to snapshot and restore. For the crossover to exist, a column would have to be roughly 13,000 tiles wide, three times the width of the entire small world it would sit in. So `[FreshWorld]` is never the cheaper option, at any width, and choosing it is a statement about semantics rather than cost: a test wants a fresh world because it asserts on something world-global, not because scrubbing a column is expensive.

**The column split cannot be calibrated, and saying so is the result.** A quarter of the world is reserved for spanning boxes that nothing in the corpus requests. It costs nothing while runs are sequential, and it stays until a suite exists that tests band boundaries. The measurement's finding is the absence, not a number.

#### What the measuring settled

**Teardown restores the ground.** Section 2.4 lists the tile rectangle first among the things release has to undo, alongside deactivating entities, replacing players, and releasing ownership. Measured at well under a millisecond for an ordinary box.

Two properties of the measuring itself decide whether the numbers mean anything.

**A watcher must not outlast the quarantine.** Observing a box for longer than it is held back shows the next occupant's work as though the previous one had never gone quiet, and reports boxes that never settle. A measured run holds boxes back for longer than it watches them.

**Restoring must not reframe.** A blank world writes its ground without framing it, so calling `WorldGen.RangeFrame` after writing recorded tiles back produces frames the snapshot never held, and every restored box then differs from the snapshot it was restored from. The recorded frames are the truth; nothing needs recomputing.

### 8.6 Tier 3, and worked examples on a real mod

The last tier and the publication gate in one milestone (section 5.1). Two processes, a virtual framebuffer for anything with a client, and fixtures for `ModPacket` round trips, `netMode` branching, and server-versus-client ownership. **Both halves are done: the two-process machinery in section 8.6a, the worked examples in section 8.6c.**

Worked examples are half the milestone rather than a garnish. Tiers 1 and 2 are trustworthy because section 8.3b's real suite against ExampleMod found three framework gaps that smoke tests miss; there is no reason to expect tier 3 to be different, and a netcode fixture nobody has pointed at real cross-process behaviour is a guess. ExampleMod is again the obvious subject: it has `ModPacket` traffic of its own, and it is maintained by the people who wrote the netcode.

### 8.6a Two processes, and what it takes to start a client

The crux of tier 3 is not the test API, it is whether a client can be started and joined with nobody at the keyboard. It can, and almost none of what that takes is where the plan expected it.

**There is no launch parameter that joins a server.** `Main.AutoJoin` exists, but nothing on the command line reaches it, and it waits for a character to be chosen regardless. So the client has to be driven from inside, by the one part of Testaria that runs on a client: a `-testariajoin host:port` flag, a fabricated character, and `Netplay.StartTcpClient`. Everything is gated on that flag, so a person playing with Testaria installed is untouched.

**A client will not start headlessly.** A server runs happily under `SDL_VIDEODRIVER=dummy`; a client started the same way exits within five seconds. It wants a real X display, so the harness starts `Xvfb` on a spare number and stops it afterwards. That is a real CI dependency and is named as one in the tool's own help.

**Three first-run screens stand in front of mod loading**, and none can be suppressed by a mod, because the mod that would suppress them has not been loaded when they appear: "Select language", then "Welcome to tModLoader", then a change-notes dialog. The logs say nothing about any of them, so they are visible only in the client's framebuffer. All three are decided from `config.json`, so the harness seeds it: a language, a version far enough ahead to count as seen, and the commit the build was made from, which the install helpfully ships in `RecentGitHubCommits.txt`.

**`ModSystem.UpdateUI` does not run at the main menu**, which is the only place the agent has work to do. A detour on `Main.Update` does, and is the same technique the server tick fix uses.

#### The shape of a tier 3 test

The body runs on the server, which owns the run, the arena and the report; the client answers questions. The alternative, running the same body on both sides and reconciling two verdicts, doubles what can go wrong and gives a failure two places to hide. Every question is a handle a test waits on rather than a value it reads, because the answer is a packet and arrives some ticks later.

**A `[NetTest]` is skipped when no client is connected, never run.** The session counts connected clients rather than trusting the harness flag that says one was launched, because a netcode test that quietly ran single-player would report a pass while proving nothing, and a client that failed to start is exactly when that would happen.

The four self-tests are: a client is connected; a packet makes the round trip and the answer identifies the client that sent it; a tile the server places *and sends* reaches the client; and a tile the server places *without sending* does not. The last pair matters more than it looks, because either alone would pass for the wrong reason.

Those four rest on one more piece of Terraria: **a client only knows the world sections it has been sent**, which are the ones near its own player. A box in the cavern is nowhere near where a client spawns, so a tile edit there means nothing to the client until it has been given the ground. `ClientLink.SendSection` gives it.

### 8.6b What ecosystem churn costs a suite

The tier 3 suite for ExampleMod is written (`tests/TestariaExampleTest/NetTests.cs`) and asserts the property that makes modded multiplayer work at all: modded content ids are assigned per load and synced, so an id meaning ExampleBlock on the server must mean ExampleBlock on the client. Nothing in it hardcodes an id; each test resolves one by name on the server and checks what the client reports back.

**A tModLoader update renamed `ProjectileID.Sets.PlayerHurtDamageIgnoresDifficultyScaling` to `SelfHurtPlayers`.** An `ExampleMod.tmod` built against the old name fails to load with a `Field not found`, and tModLoader disables it. Rebuilding does not help while the local tModLoader checkout ExampleMod's source comes from predates the same change and still uses the old name, with no `SelfHurtPlayers` anywhere in it.

So the fix is outside this repository: update the tModLoader checkout to at least the commit the install was built from (`39e7995f`, "Document and fix patches for new Projectile.SelfHurtPlayers method"), then rebuild ExampleMod with `scripts/build-examplemod.sh`. That is somebody else's working tree, so it is theirs to do.

#### What that costs a run, and the guard it needs

`Subject.Require()` skips when the mod under test is absent, which is the honest answer per test, and 924 correct skips sum to a run that reports success while testing nothing.

So a run can be told the fewest tests that must actually run rather than skip, with `--require <n>` on the CLI and `MIN_TESTS` in the shell harness, and falling short is a failure whose message names the likely cause. The calibration gate asks for 100. Section 8.6e carries the stronger form of the same guard, which names the mods themselves rather than counting.

This is the section 2.2 problem in a different costume. There, a test could pass while proving nothing; here, a whole suite could. Honest skipping is still right, and it needs a backstop that counts.

### 8.6c The tier 3 worked examples

The checkout has to match the install the mod will load into, which for ExampleMod means the commit the installed build came from (`39e7995f`), and `scripts/build-examplemod.sh` rebuilds it from there. The calibration suite reports **927 tests, 924 passed, 3 skipped**, the three being the tier 3 tests when no client is attached.

With a client attached, all three pass: a modded tile placed by the server arrives at the client as the same content, a modded NPC spawned by the server appears in the same slot with the same type, and an empty slot reads empty on both sides. The property they establish is the one that makes modded multiplayer work at all, and none of them hardcodes an id: each resolves one by name on the server and checks what the client reports back.

### 8.6d What a client has received, and when

Tier 3 rests on knowing what a client can be asked about, which is narrower than it looks: **a client answers "no tile" for everything in a section it has not been given yet**, and an unreceived section is indistinguishable from empty ground. Anything built on top of that has to establish agreement rather than assume it.

**Who sends sections.** `NetTrace`, behind `-testariatracenet`, hooks `NetMessage.SendSection` and `NetMessage.SendTileSquare` on the server and logs each call with the stack that produced it. Over a full tier 3 run there are exactly two senders:

- Testaria's own `ClientLink.SendSection`, which a test asks for.
- `MessageBuffer.GetData` handling **message 8, the client's own request for the world around its spawn** (`MessageBuffer.cs:664` onwards). Measured: nine sections, a three by three block covering tiles 0 to 599 by 0 to 449, all sent at the tick the client joined.

Nothing resends. `SendSection` returns immediately for a section the client already has, which the trace labels, and no other caller fires in a traced run.

**When those sections arrive is the part that matters.** Measured with a probe, the client first sees a tile inside its own spawn block **five ticks after the suite starts** if the harness begins on the server's "has joined" line, which is the server accepting a connection rather than the client being in the world. A test that edits a tile in that block during the window sees the section turn up afterwards carrying the edit, which looks exactly like the server having sent something nobody asked for.

So the client tells the server when it is in the world, and the harness waits for that rather than for "has joined". The honest limit is recorded rather than papered over: **being in the world does not mean the world has arrived.** Terraria streams sections continuously, so there is no moment at which a client is finished receiving; measured, the spawn block still completes nine ticks into the run.

The per-test answer is `ClientLink.AwaitSection`, which sends a section and the tile square with it, then waits until both sides agree about the ground a test is about to touch. A fixed wait reads its own impatience; waiting for agreement reads the network. There is no harness-level substitute for it.

**A control that does not depend on timing.** The client is asked about ground far from the arena that nothing has sent it, and answers "nothing" while the server sees stone. That establishes that the answers are the client's own view rather than an echo, which is the only thing such a control is needed for.

### 8.6e Calibration against the ecosystem, and what it found

Every gate up to here aims the framework at ExampleMod and at itself. Both are unusual subjects: ExampleMod is maintained by the people who maintain the loader, and the self-test mod was written by the framework's author to exercise the framework. Neither can say what happens when a stranger's mod arrives.

So the framework is pointed at **every published mod with active 1.4.5 work**, taken from a survey of the fifty most-subscribed mods on the Workshop. There are six. Ranks 1 to 10 have none at all.

**Two of the six build and load as found.** InnoVault needs nothing; DAYBREAK compiles and then throws during its load pass. The other four do not compile. After a deliberately small amount of porting, four of six build and load; one is blocked by a version skew between two of its own dependencies, and one needs the `WorldItem` port throughout, which is real work rather than renames.

The shape of those failures matters more than the count. Four of the six are renames or a single moved method. What makes them expensive is not their size but that the 1.4.5 line moves underneath the mods targeting it: `FocusHelper.AllowUIInputs` and `AllowGameplayInputs` stopped existing in the 1.4.5.8 update of 2026-09-16, and SilkyUI's branch carries commits from a week *after* that update that still reference them. This is risk 7 in the ecosystem rather than in this repository.

**Every mod that works has a suite**: 112 tests for InnoVault, 16 for DAYBREAK, 21 for Cheat Sheet and 28 for SilkyUI, 177 in all, across four mods nobody here wrote. They establish both halves of what follows: the capabilities a foreign mod needs, and four limits that belong to the ecosystem or to the design rather than to anything a framework can answer. The full account, with the evidence for each, is in `docs/ecosystem-calibration.md`.

The four worth recording here:

**A run insists on its subject.** tModLoader disables a mod that throws during its load pass and carries on without it, so a suite aimed at that mod goes down with it: the run discovers nothing, prints `0 tests: 0 passed` and exits 0, which is indistinguishable from a suite that passed. The harness tells the run which mods it installed, with `-testariarequiremods`, and the run refuses to start if any is absent, naming it. A run that discovers no tests at all is an error for the same reason.

**A client keeps the sections it has been given.** Terraria remembers which ones a client has, and `SendSection` returns immediately for one already sent, so a tile the server changes locally afterwards, which is every tile a test touches, is never corrected on the client: the two sides then disagree for as long as the test is willing to wait. `AwaitSection` therefore sends the tile square alongside the section, which is what makes it work for every test in a section rather than the first.

**A client can be asked about a mod's own state, not only vanilla's.** A mod's synced state is the entire reason a mod has netcode, and the missing piece is the extension point rather than the transport. A test mod is loaded on both sides, so the code that knows how to inspect a mod's own objects already sits on the client. `ClientQuery.Register` names a handler during a load pass and `ClientLink.Ask` calls it by name. InnoVault's suite now tests TileProcessor replication end to end: a processor the server creates reaching the client, its own `SendData` payload arriving with it, repeated syncing not multiplying it, and a client knowing nothing of one it was never told about.

**A gate has to find its subject by something the subject declares.** The report carries `testaria-tier` on each `testcase` and the tier 3 gate reads it, so a tier 3 test written in a class called anything at all is still covered. Selecting by naming convention is the version of this that stops working without saying so, and it takes the whole suite's coverage with it: running everything with a client attached is also what catches a tier 2 test that counts the connected client's player among the ones it fabricated.

**A client-only mod is reachable, drawing included.** Every tier runs its test body on a server, and a server draws nothing, so the reach comes from the other side: the tier 3 client is a whole game process on a real framebuffer, and a client query runs arbitrary mod code inside it. Measured from a query running there, the client has a graphics device, an 800x720 back buffer, working render targets, a sprite batch, loaded fonts and a live `LocalPlayer`. Two self-tests read back pixels the GPU drew.

Which is why section 2.5 scopes out rendering **tooling** rather than rendering: image comparison, golden files, and input replay are what is missing. The door is open and nothing has been built through it, which is the honest position to record.

The ratio is still poor: of Cheat Sheet's whole surface, one `GlobalNPC` runs on a server. "Poor ratio, reachable through a query" is a different statement from "cannot be tested".

**One finding really is about the ecosystem rather than this framework.** A suite is a separate assembly by construction, so a mod that keeps its types `internal`, which is the right default, is invisible to its own tests until it says `[assembly: InternalsVisibleTo]`. That belongs in the documentation, and it bears on section 5's expectations about who will adopt this.

**What this adds to the plan.** Section 2.4 wants a real test corpus to adjudicate the isolation design, and there is one, partly: boxes hold across 128 tests on two foreign mods with no cross-contamination. But a box isolates a region and nothing else, and a test that flips a static field needs `ctx.Restore` rather than geometry. That is built, and is the fifth mechanism section 2.4 does not anticipate.

### 8.6f Configuring the mod under test

Configuration is a first-class tModLoader feature and section 0.1 lists it among what tier 1 can test, so a run has to be able to set it. Otherwise a suite can only ever test a mod's defaults.

Worse, a default can make a mod untestable. SilkyUI enables a blur effect by default; on a machine whose build cannot compile the shader, the client dies at its first frame and every tier 3 test against it times out.

`--config <path>` seeds a file, for the same reason `enabled.json` is written by hand rather than through a menu: it is what the game reads, and nothing about it needs Steam or a person. The file's own name is the contract, `<ModName>_<ConfigClassName>.json`, and a misnamed one is refused up front rather than ignored by the game and discovered as a run that quietly tested defaults. Each file is copied into both the client and the server config directories, because the game reads a `ClientSide` config from one and a `ServerSide` config from the other and ignores what it cannot match, so the caller is never asked which scope somebody else's mod used.

### 8.6g Using what the templates produce

`check-packages.sh` consumes the packages the way a stranger would, because unit tests cannot see packaging at all. The templates need the same treatment: nothing else compiles a generated project, let alone runs one.

An onboarding step nobody executes is an onboarding step nobody has checked: a suite can stop its own mod from compiling, a mod's internals can be invisible to its own tests, and a template's post action can make the template fail to install while `dotnet new` says nothing and simply lists one template instead of two.

`check-templates.sh` packs `Testaria.Core` into a throwaway feed, checks that the version the templates pin is the version packing produces, installs both templates, generates from each, runs the tier 0 project's tests, and builds the mod-tests project into a real `.tmod`. All of it passes.

Two details it handles. The pinned version is checked because a mismatch makes every generated project fail to restore with an error naming NuGet rather than the mismatch. And `tMLMod.targets` builds a mod by invoking tModLoader, which writes the `.tmod` into the save path's `Mods` folder and offers no way to redirect it, so the probe mod is named distinctively and removed on the way out: a gate must not leave a mod installed.

### 8.6h CI for the game tiers, on a machine that already has the game

Section 8.7 waits on a GitHub release, and everything section 8.3a lists for that job (download the server zip, hold an ownership key as a secret, decompile, build, cache) exists to reconstruct on a rented machine what a developer's own machine already has. None of it is what makes a run *continuous integration*. So the discipline can be applied here first, and the job that ships in 8.7 becomes a port of a recipe that has been running rather than a first attempt.

`scripts/ci-local.sh` is that recipe. `run-all.sh` remains the developer's loop; two environment variables turn it into the body of a run, and the wrapper supplies the rest.

**Strict mode, because skipping is the failure mode that matters.** `run-all.sh` had five ways to reduce itself: four `SKIP_*` variables and a calibration gate that skipped itself when no `ExampleMod.tmod` was present. Every one of them prints a line and exits zero, which is the same shape as the bug section 8.5 already named twice: a suite that skips everything is not a suite that passed. `STRICT=1` makes a gate that will not run a gate that failed. The load gate is the one exception and says so: it measures rather than asserts, so a loaded machine makes it report a slower arena rather than a broken one.

**Provisioning, because a calibration subject has to be a known one.** The gate consumed whatever `ExampleMod.tmod` was last built, by anything, at any time. A run builds it first, which the incremental cache in `build-examplemod.sh` makes cheap.

**A run directory, because a verdict that is only a terminal is not a record.** `RESULTS_DIR` collects one log per gate, the JUnit report from every gate that writes one (the green path and the calibration through `RESULTS_OUT`, which already existed; the red path and both halves of tier 3 through a copy on the way out, which did not), `gates.tsv`, and a summary counting tests as well as gates. Counting only gates would hide exactly what `MIN_TESTS` exists to catch.

**A lock and a history, because that is the difference between a script and a service.** Two runs share one save directory and one port, so the second fails in ways that look like the framework's fault. `history.tsv` records which commit got which verdict, and `--if-new` reads it: a timer fires every fifteen minutes, finds nothing to do, and costs a `rev-parse` and a `grep`. It declines a dirty tree rather than testing a moving target, since a scheduled run records a verdict against a commit and a tree with edits in it is not any commit.

`scripts/systemd/` holds a user service and timer. The service reads its one machine-specific value from `~/.config/testaria-ci.env`, for the reason `scripts/paths.local.sh` is untracked.

**Measured on this machine:** all eight gates green in 286 seconds, on a machine at a load average of 14 with another game running. 1107 tests meant to pass, of which 1068 did and 39 skipped themselves for want of a client or a fresh world, plus the red path's six deliberate failures. The slowest gate is tier 3 at 81 seconds and the calibration's 927 tests take 29. That is the first evidence in this document that the whole matrix passes from one unattended entry point rather than from a developer's shell, and it says the cost of running everything is five minutes rather than an afternoon.

The red path is the reason the summary counts two totals rather than one. Its six tests fail and error on purpose, and folding them into the rest prints "2 failed, 4 errored" directly above a verdict of pass, which reads as a broken run. They are reported as what they are instead.

**One thing it deliberately does not solve.** Building a mod means invoking tModLoader, which writes the `.tmod` into the save path's `Mods` folder and offers no way to redirect it (section 8.6g met the same wall). A run therefore shares that folder with whatever else uses it. The gates themselves run in a scratch save directory and cannot touch a real world or player, but the `Mods` folder is common ground, and a scheduled run that fires while somebody is playing will replace the mods they have installed. Fixing it means a redirect tModLoader does not offer.

Linux only, as written: `Xvfb`, `systemd-run`, and `flock` all appear in it. The portability that matters is already asserted, on three operating systems, by the core job in section 8.3a.

### 8.7 CI for the game tiers, shipped with the first GitHub release

One change, not two (section 5.1, item 3). The CI job follows section 8.3a: derive the ownership key once from an owned install and store it as a repository secret, download the public server zip from terraria.org, decompile and build tModLoader with the result cached as tModLoader's own CI caches it, then build the mods and run the gates.

What it runs is settled by section 8.6h: `scripts/ci-local.sh`, which already preflights, provisions, runs every gate strictly, and keeps the reports. The hosted job's own work is the four steps above, which end with a tModLoader install and an `EXAMPLEMOD_SRC`, at which point it has what the local recipe assumes. The parts of `ci-local.sh` that are a developer's machine rather than CI, the lock and the commit history, are harmless there: a hosted runner is alone on its own filesystem and its checkout is always the commit under test.

The release half is artifact A as a GitHub Release with a full SemVer tag, plus whatever `docs/` has by then. The point of pairing them is that the release is only worth making if a machine other than a developer's own has run the gates that produced it.

### 8.8 nuget.org, once GitHub is working

Artifacts B through E to nuget.org with the `Testaria.*` ID prefix reserved, after the GitHub channel has proved itself. Last because it is the least reversible step in the plan: an ID can be unlisted but never deleted, and by this point the names have been carried by a working release rather than by an intention.

### 8.9 Rendering, once there is something to publish

Deliberately after 8.7 and 8.8. Section 5.1 gates publication on tier 3 working with worked examples, and it does; adding a fifth thing to build before anyone can install the first four would be the same mistake section 8.1 avoided by starting at tier 1 rather than tier 0.

**The reach already exists. The tooling does not.** Section 8.6e records what is reachable: a `[NetTest]` body runs on the server, but a `ClientQuery` runs arbitrary mod code inside a client process that has a graphics device, an 800x720 back buffer, working render targets, a sprite batch, loaded fonts and a live `LocalPlayer`. Two self-tests read back pixels a GPU drew. So this section is not about access. It is about the three things that turn "I can get pixels" into "I can test what my mod draws".

**8.9a Capturing a frame rather than a target.** The self-tests draw into a `RenderTarget2D` they made themselves, which tests drawing code that was written to take a target. Most drawing code is not: it draws into whatever the game is drawing into, at a point in `Main`'s draw order. Capturing that means running a real frame and reading the back buffer, or redirecting the interface layer into a target for one frame. The second is likely, because it is what mods doing their own compositing already do, but it is the piece that needs designing rather than merely writing.

**8.9b Comparing images, and what a failure looks like.** Pixel equality is the wrong default: a GPU driver, a screen size, and a font all move pixels without anything being wrong. The useful comparisons are a tolerance, a structural one such as "this region is not blank", and a golden image. Golden images bring their own problems, all of them well known: they have to be generated on some machine and trusted, they are binary in a repository, and a failure has to be inspectable or nobody will ever look. A failure that says "0.4% of pixels differ" and cannot show which is not worth having, which is a reason to hold this until there is somewhere for artifacts to go. That is section 8.7's CI job, which is another reason this comes after it.

**8.9c Input, and whether it belongs at all.** Driving a mouse and keyboard on the client is a separate mechanism from either of the above, and a smaller one: `Main.mouseX`, `Main.mouseLeft` and the input state are ordinary fields, and a client query can set them. What is not obvious is whether a test that does so is testing anything durable, or is pinning the exact frame on which a click is noticed. Worth a prototype before it is worth a design.

Section 2.5 scopes this out as tooling rather than as reach, and the README says the same: what the self-tests establish, and that what remains is tooling. That is the honest position and it costs nothing to hold while 8.7 and 8.8 happen.

**One dependency worth recording.** A client is a whole game process and meets failures a server never does. A mod whose shader asset cannot be compiled on the build machine stops its client at the first frame that wants it, long after the harness has watched it join, and every tier 3 test then waits out its budget. That is why `--config` exists (section 8.6f): a run seeds the mod's configuration, turns the drawing off, and keeps the client alive. Anything in 8.9 that draws will meet this class of problem constantly, since drawing is where a client's dependencies actually get used.

