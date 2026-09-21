# A testing framework for Terraria mods: naming, scope, packaging, and distribution

Status: plan, not yet implemented. Written against a checkout of [tModLoader](https://github.com/tModLoader/tModLoader) on branch `1.4.5` at HEAD `7f5a46e98d`. Paths given below as `patches/...` are relative to the root of that checkout.

## 0. The short version

- **It is not one product, it is four tiers** across two execution environments. Tier 0 (pure unit) runs in a normal `dotnet test` host. Tiers 1 through 3 (loaded, world, multi-process) must run inside a patched tModLoader process. Conflating them is the main design trap.
- **It is not one artifact, it is five.** tModLoader mods cannot consume NuGet, and .NET test projects cannot consume `.tmod`. The in-game runtime ships as a `.tmod`; the developer-facing toolchain ships as NuGet packages. One brand, two package systems.
- **Recommended brand: `Testaria`**, a coined portmanteau reused across the mod internal name, root namespace, NuGet ID prefix, and repo name. This is the only genuinely irreversible decision in the plan.
- **Primary distribution is GitHub Releases plus nuget.org, not the Steam Workshop.** The Workshop is a player channel, it cannot express prerelease versions, and test mods are never meant to reach players.
- **Boxes are leased and recycled, so the arena scales with concurrency, not suite size.** Two kinds: banded boxes inside one layer, and spanning columns for tests whose subject *is* a layer boundary. Isolation needs four layered mechanisms (geometry, an ownership warden, pool budgeting, declared global effects), all provisional until a real test corpus can adjudicate them (section 2.4).
- **First milestone is Tier 1, not Tier 0**, because Tier 0 already works today with a stock MSTest project and proves nothing new.

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

Mitigations, in order of preference:

1. Ship Tier 0 helpers in a package that does **not** transitively reference `tModLoader.dll`, so the dangerous types are simply not in scope. This is the clean answer where it is achievable.
2. Where a reference is unavoidable, ship a Roslyn analyzer that errors on use of the loader-dependent surface from a Tier 0 assembly. tModLoader already ships analyzers this way (`tMLMod.targets:86-87`), so the pattern is familiar to users.
3. At minimum, document the boundary loudly and provide a `[RequiresLoadedGame]` marker that fails fast rather than silently.

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
| CLI command | `testaria` (see section 4.4.6 on why the package is not named `dotnet-testaria`) |
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
| E | `Testaria.Templates` | NuGet `dotnet new` template pack | Scaffolds a test mod plus a test project, with a **working placeholder test at every tier** (see 4.4.7) | Onboarding |

B through E are optional in v1 and can be collapsed; A and D are the minimum viable pair.

### 4.3 How a mod author consumes A

Two shapes, and the choice has real consequences for players:

**Recommended default: a separate test mod.** `MyModTests` is its own mod folder with `modReferences = MyMod, Testaria` and is never published. The player-facing `MyMod` carries no test code and no dependency on the framework, so no player is ever asked to download `Testaria`. This is the clean separation and it costs one extra folder.

**Alternative: tests inside the mod, conditionally compiled.** Tests live under `Tests/` in the mod, guarded by `#if TESTARIA`, with the define passed through `ExtraBuildModFlags` into the `-define` argument (`tMLMod.targets:102`). `buildIgnore = Tests\*` excludes the folder from a release build. Cheaper for a small mod, but it leaks test source into the shipped archive if `includeSource = true`, and requires `weakReferences = Testaria` so the release build does not force the dependency. Offer it, do not default to it.

### 4.4 Packaging decisions to lock in

1. **Do not host xUnit, NUnit, or VSTest in-game.** Section 1.1.4 makes it impractical. Write reflection-based discovery over `AssemblyManager.GetLoadableTypes`. But deliberately **mirror xUnit's vocabulary** (a `[Fact]`-shaped attribute, a `[Theory]`-shaped attribute, `Assert.*` with the same method names and argument order) so the surface is learnable in five minutes, and **emit JUnit XML** so CI already understands the output without a custom reporter.
2. **Keep bundled managed dependencies at zero if possible.** The mechanism exists (`lib/` plus `dllReferences`, and it is transitive to dependents per section 1.1.3), but every bundled DLL is another memory load on every reload and another surface under `ModUploadRules` rule 3.
3. **Write results only under `<SavePath>/Testaria/`.** Rule 2 requires it for anything published, and designing to it from the start keeps Workshop publication available as an option even if you never exercise it.
4. **B must set its own `TargetFramework` and `LangVersion`**, because `tMLMod.targets` only sets them under `BuildMod=true`.
5. **License MIT**, matching tModLoader and the ecosystem norm, and keeping the door open to upstreaming. Note again that `tml-build` is AGPL-3.0 and must not be vendored.
6. **Name the CLI package `Testaria.Tool`. This is a weak preference over `Testaria.CLI`, not a convention.** Per section 1.2.4 there is no official guidance and the ecosystem is split, so the only real constraint is that the ID stay under the `Testaria.*` root, which both candidates satisfy and `dotnet-testaria` would not (it would sit outside the ID prefix reservation and remain squattable).

    The tiebreaker for `.Tool`: this package will have sibling *library* packages under the same brand (`Testaria.Unit`, `Testaria.Sdk`, `Testaria.Templates`), and disambiguating the installable tool from the libraries is precisely the job `.Tool` was adopted for by Cake and GitVersion, both of which faced the identical situation. `.CLI` is weaker here because it is used for both tools and plain libraries, so it does not answer "can I `dotnet tool install` this?" on sight.

    The counter-argument is real and worth recording: `.CLI` reads better in prose, and nobody says "the Testaria Tool". That costs nothing, though, because prose can call it "the Testaria CLI" regardless of the package ID, and what anyone actually types is `testaria`, set by `<ToolCommandName>`. If `.CLI` is preferred on taste, nothing else in this plan changes.
7. **The template must ship a working placeholder at every tier, not just Tier 0.** A Tier 0-only template would actively teach the failure mode described in section 2.2, by implying that the default home for a test is the out-of-game project. It should scaffold one Tier 0 test, one Tier 1 test, and one Tier 2 boxed test, each with a comment stating *why it lives at that tier*, so the boundary is learned from a working example rather than from documentation nobody reads. The second payoff is diagnostic: environment setup is the hardest part of adopting this framework, and a scaffold that goes green end to end proves the tML install path, scratch save directory, and world provisioning all work before the author has written a line of their own. Tier 3 stays out of the default template, since it needs a second process, and gets its own opt-in template.

## 5. Distribution

| Artifact | Primary channel | Secondary | Rationale |
| --- | --- | --- | --- |
| A, the `.tmod` | **GitHub Releases** | Steam Workshop, optional and later | The audience is developers who already have a checkout. GitHub Releases can carry full SemVer tags, and CI can install with `curl` plus a copy into `<SavePath>/Mods/` plus a line of JSON (section 1.1.8). The Workshop cannot express prerelease versions and requires Steam plus either the in-game UI or SteamCMD with `-ciprep` |
| B, C, E | **nuget.org**, with the `Testaria.*` ID prefix reserved | GitHub Packages for prereleases | Standard .NET distribution |
| D | **nuget.org** as a dotnet global tool | | `dotnet tool install -g Testaria.Tool`, invoked as `testaria` |
| Documentation | **GitHub Pages built from `docs/`** via DocFX | Cross-link from the tModLoader wiki | A wiki lives in a separate `*.wiki.git` repo, so it cannot be versioned with the code, reviewed in a PR, or changed atomically with the API it documents. For a framework whose surface will churn with the 1.4.5 port, that drift is the predictable failure. `docs/` also lets contributions arrive as PRs, lets code samples be compile-checked in CI, and lets a merge be gated on a docs update. DocFX is the .NET-native generator and pulls API reference straight from XML doc comments, which the public attribute and assertion surface should carry anyway. tModLoader's own generated docs at docs.tmodloader.net set the precedent |
| Announcement | Terraria Community Forums thread, tModLoader Discord `#modding` | | Draft only; do not post without explicit instruction |

The asymmetry worth internalizing: **GitHub is the right primary for the `.tmod` precisely because local mod loading does not require Steam.** Publish to the Workshop only if you later want end users of published test mods to get dependency resolution automatically, which, if test mods are never published, you never need.

## 6. Versioning

Three clocks, kept deliberately separate:

- **NuGet packages:** full SemVer 2.0, prerelease tags allowed, normal .NET expectations.
- **The `.tmod`:** `build.txt` `version` is 2 to 4 integers and cannot carry a prerelease tag. Map `1.2.3` to `1.2.3` and use the fourth component for prerelease iterations (`1.2.3.1`), keeping the real SemVer string in the git tag and the release notes.
- **tModLoader compatibility: 1.4.5 only.** Settled. The 1.4.4 and 1.4.5 target frameworks are incompatible (section 1.1.14), and supporting both would mean two branches, two CI matrices, and two sets of release artifacts. Building against the line the framework will live on avoids paying a porting cost twice. If 1.4.4 support is ever required, mirror tModLoader's own branch-per-target structure rather than trying to multi-target a single project.

`modReferences = Testaria@1.2` is the only compatibility enforcement the Workshop offers, so keep the minimum-version discipline tight and treat any change to the attribute or assertion surface as a minor bump at minimum.

## 7. Risks and open decisions

1. **Naming is the only irreversible decision here.** Mod internal names are globally first come, and renaming a published mod orphans its subscribers. NuGet IDs can be unlisted but never deleted. Settle the brand before publishing anything to any channel.
2. **False confidence at the Tier 0 boundary** (section 2.2). This is the highest-severity design risk, because the failure mode is a green test suite that proves nothing.
3. **World state isolation between Tier 2 tests** is the hardest engineering problem, and section 2.4 sets out a four-mechanism working design that is explicitly provisional pending real test surface. Five numbers in it are unverified and must be measured rather than hardcoded on a guess: the `Main.SceneMetrics` scan radius, which sets the gutter for any biome-sensitive test; the default banded box size; the quarantine duration before a released box is safe to re-lease, which is longer for columns than for banded boxes; the split between the banded and column arena regions; and the width at which a spanning column stops being cheaper than `[FreshWorld]`.
4. **Reload safety.** Every hook, event subscription, and static registration the framework makes must be undone in `Unload()`, or it pins dead `AssemblyLoadContext` instances (`AssemblyManager.cs:177`, `196`). A test framework that leaks across reloads will be blamed for the leaks of the mods it tests.
5. **Nondeterminism.** Seed control over `Main.rand` and `WorldGen.genRand` must be a day-one feature.
6. **tModLoader is a moving target.** The 1.4.5 port is in progress; `MigrationGuide_1.4.5.md` and `PortingNotes_1.4.5.md` are live documents. Expect churn in whatever hooks the framework attaches to, and expect to run `tModPorter` more than once.
7. **Upstreaming.** The TML team currently has only hand-driven failure-case mods in `test/Test Local/`. If this framework works, they may want it in-tree. Keeping it MIT and structurally separable from any one mod preserves that option at no cost.
8. **Test mods under `ModSources`.** `ModSources` lives under the shared stable save path, so all build purposes (Stable, Preview, Dev) share it. A test mod placed there is visible to every tModLoader install on the machine, which is convenient for iteration and surprising if unexpected.

## 8. First milestone

Deliberately Tier 1, not Tier 0: a Tier 0 suite is already achievable today with a stock MSTest project and `BuildMod=false`, so building it first proves nothing that section 1.1.5 does not already prove. The novel value starts the moment a test needs the loader to have run.

Milestone 1, which exercises the entire spine end to end:

1. `Testaria` as a minimal library mod: a `[GameTest]` attribute, reflection discovery over loaded mod assemblies, and a runner that executes discovered tests in sequence.
2. A `ModCommand` with `CommandType.Console`, so a `-server` process can be driven from stdin.
3. A minimal `Assert` surface and a JUnit XML writer targeting `<SavePath>/Testaria/`.
4. A shell script that downloads a tModLoader release, provisions a scratch save directory via `-tmlsavedirectory`, drops in the `.tmod` files, writes `enabled.json`, launches `-server` with `-autocreate`, pipes the run command, and exits nonzero on failure.
5. One real assertion against `ExampleMod`, proving the loop is closed.

That yields a red or green result in CI. Everything after it is filling in tiers against a spine that is already known to work.
