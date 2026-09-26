# Calibrating against the 1.4.5 ecosystem

Testaria pointed at every published mod with active tModLoader 1.4.5 work,
rather than at ExampleMod and its own self-tests.

Run on 2026-09-24 against tModLoader `1.4.5.8+9999.0|2026.07|1.4.5|dev`,
commit `39e7995f`, built the same day and the newest dev build available.

## The corpus

Six repositories, taken from a survey of the fifty most-subscribed mods on
the Workshop. Ranks 1 to 10 have no 1.4.5 work at all; these six are the
whole of it.

As found, and then as left after a deliberately small amount of porting.

| Mod | Branch | As found | After | What it took |
|---|---|---|---|---|
| InnoVault | `tml145` | builds, loads | unchanged | Nothing. The one subject that needs no work at all. |
| DAYBREAK | `1.4.5` | builds, fails to load | builds, loads | One IL hook retargeted from `Main.DoUpdateInWorld`, which 1.4.5.8 emptied, to `Main.UpdateWorld_NPCs`, where the loop it wants now lives. |
| Cheat Sheet | `1.4.5` | 6 errors | builds, loads | Renames: `NewItem`'s trailing bool became `NewItemOwnership`, `Main.item` holds `WorldItem` so `newAndShiny` is reached through `.inner` and `SetDefaults` became `TurnToAir`, `FocusHelper.AllowGameplayInputs` became `GameplayActive`. |
| SilkyUI | `1.4.5` | 18 errors | builds, loads | `FocusHelper.AllowUIInputs` to `AllowInputProcessing`, plus its XML components renamed to `*.sui.xml` and their `Class` and `Name` attributes moved into the generator's namespace. Needs SilkyUIAnalyzer at `827450e`, not `main`. |
| Quality of Terraria | `1.4.5` | blocked | still blocked | Wants a SilkyUI with `Common.Tweening` and `StyleSystem`, which is the `1.4.5-preview` branch, not the `1.4.5` branch its sibling is on. A version skew between two of its own dependencies. |
| Fargo's Mutant Mod | `1.4.5` | 23 errors | still 22 | One typo fix, `ShowExcalmation` to `ShowExclamation`, which vanilla stopped sharing. The rest is the `WorldItem` and `NewItem` port throughout, which is real work rather than renames. |

So **two of six can be tested as found**, and **four of six after a
deliberately small amount of porting**. That is the ecosystem Testaria is
aiming at, and it is worth knowing before concluding anything from a corpus of
one mod plus ExampleMod.

The shape of the failures is worth as much as the count. Four of the six are
renames or a single moved method, and only one is genuine porting work. What
makes them expensive is not their size but that the 1.4.5 line moves under the
mods targeting it: SilkyUI's branch carries commits from a week after the
update that broke it.

Two of the four failures share a cause. `FocusHelper.AllowUIInputs` and
`FocusHelper.AllowGameplayInputs` stopped existing in tModLoader's 1.4.5.8
update (2026-09-16, commit `ebf5e40be7`), and both Cheat Sheet and SilkyUI
still reference them. SilkyUI's branch carries commits from a week *after*
that update, which says how fast the 1.4.5 line moves under the mods targeting
it.

Daybreak's load failure is the same one layer down: it rewrites the
`npc.townNPC` load inside `Main.DoUpdateInWorld`, and 1.4.5.8 cut that method
down to a wrapper and moved the world update into `UpdateWorld_*` methods. The
loop it wants is now in `Main.UpdateWorld_NPCs`. Retargeting the hook there is
the whole fix.

## The suites

Both live on a `testaria-tests` branch in their own repository.

- **InnoVault**, 112 tests. Tier 1 covers the easing curves and the geometry
  and byte helpers in `VaultUtils`, parameterised so each curve is its own
  reported case, plus the type registries. Tier 2 covers the TileProcessor
  lifecycle in a leased box: attach, initialise once, tick once per tick, die
  with the tile, stop ticking once dead. Tier 3 covers replication: a
  processor the server creates reaching the client, its own data arriving
  with it, syncing repeatedly not multiplying it, and a client knowing
  nothing of one it was never told about. All 112 pass with a client
  attached; without one the five tier 3 tests skip.
- **DAYBREAK**, 16 tests. Tier 1 covers the shape of the NPC ID sets. Tier 2
  covers the IL edit behind `VulnerableToAfterPartyOfDoom`, with a control:
  the same NPC in the same routine dies when the set says nothing and lives
  when the set says no. All 16 pass.

Cheat Sheet and SilkyUI are ported far enough to build and load, and have
suites of their own:

- **Cheat Sheet**, 21 tests. It is a client-side cheat menu, so nearly all of
  it is UI and does not exist on a server. What does is the NPC filter, a
  `GlobalNPC` that kills any NPC whose net ID is listed, and that is real
  gameplay in a real world: tier 2 filters a type, spawns one, and watches it
  die, with two controls. Tier 2 also covers `TileData`, the five components
  the paint tools lift off each tile, whose failure mode is silent and
  permanent. Tier 1 covers the stamp chunking arithmetic.
- **SilkyUI**, 28 tests. A UI framework on a headless server sounds like a
  contradiction and mostly is, but the value types the engine is built out of
  and the flexbox engine itself are both pure computation, and the engine is
  the one thing in a UI framework really worth testing. Three more run inside a
  real client and cover the pipeline the server cannot: the framework's own
  entry point, producing real screen rectangles. Those need SilkyUI's blur
  turned off, which a run does by seeding the mod's own config.

177 tests across four mods nobody here wrote. They are where the findings
below come from.

## Findings

Four findings, every one of them a limit rather than a defect: two belong to the ecosystem, one to the standard tModLoader layout, and one to what a box can mean at all. Where they stand:

| | Finding | Status |
|---|---|---|
| 1 | Adding a suite to a mod's repository breaks the mod's build | documented |
| 2 | A box cannot contain world-global state | documented; not fixable in general |
| 3 | Tier 0 is out of reach for a normal mod | documented |
| 4 | A mod that keeps its types internal cannot be tested without opting in | documented |
| 5 | A mod can build cleanly and produce a `.tmod` that cannot load | documented |
| 6 | Building a mod rewrites `ModSources/tModLoader.targets` outside any scratch | documented; ours to fix |
| 7 | Two sibling properties, one defensive and one not | reported to the subject |

### 1. Adding a suite to a mod's own repository breaks the mod's build

The README says a test suite belongs with the mod it tests. The standard
tModLoader layout puts the mod's `.csproj` at the repository root, where the
SDK's default `**/*.cs` glob picks up everything beneath it. Creating
`InnoVaultTests/` under it produces 127 errors, all of them attributed to
`InnoVault.csproj`, because the suite is compiled into the mod.

The fix is two lines, and neither the templates nor the docs mention it:

```xml
<ItemGroup>
  <Compile Remove="MyModTests\**" />
  <None Remove="MyModTests\**" />
  <AdditionalFiles Remove="MyModTests\**" />
</ItemGroup>
```

plus a `buildIgnore` entry, or the suite's sources ship inside the mod's own
`.tmod`.

Anyone following the README's advice with `dotnet new testaria-mod-tests -n
MyModTests` inside their mod folder hits this immediately, and the error
messages point at their mod rather than at anything they just did.

### 2. A box cannot contain world-global state

Not every kind of leakage is spatial. `Main.afterPartyOfDoom` triggers
a vanilla routine that sweeps **every** town NPC in the world, not the ones
inside a box. The arena isolates a region; it has nothing to say about a
global flag or a world-wide sweep.

The escape and contamination machinery watches entities crossing a boundary,
which is the right guard for entity-shaped leakage, and there is no
equivalent for this. It is probably not fixable in general, but it is worth
being explicit about in the docs: a box is a region, and "isolated" means
isolated in space.

### 3. Tier 0 is out of reach for a normal mod

InnoVault has thousands of lines of genuinely pure logic: easing curves,
vector and rectangle maths, a byte splitter. Textbook tier 0 material. None
of it can run in a tier 0 host, because it lives in the mod assembly and a
mod assembly only exists inside a loaded game. All of it is tier 1.

The tier 0 boundary machinery, the analyzer and `Testaria.Unit`, protects
code that has already been factored into an ordinary library. Almost no
Terraria mod does that. So for most mods the practical floor is tier 1, at
roughly eight seconds of server start per run.

Two things follow. The docs should say this plainly, so that nobody reads the
tier table and expects to put their mod's logic in tier 0. And the speed of
tier 1 matters more than the tier 0 story does, because tier 1 is where the
cheap tests of a real mod actually live.

### 4. A mod that keeps its types internal cannot be tested without opting in

A test suite is a separate assembly by construction: it is a separate `.tmod`
that the loader enables on its own. So a mod that keeps its types `internal`,
which is the right default for a mod, is invisible to its own tests.

Cheat Sheet keeps essentially everything internal, including its `Mod` class.
A suite referencing it can see one public record struct.

The answer is one line, `[assembly: InternalsVisibleTo("MyModTests")]`, and it
is worth documenting rather than leaving people to discover: the obvious
alternative, making types public so they can be tested, changes the mod's own
surface for the sake of its tests.

## What works

Worth recording, because the list above is all limits.

- `build/Testaria.props` carries an out-of-repo suite with no trouble once
  `TESTARIA_PATH` is set. Both suites use it and nothing else.
- `[CaseSource]` earns its place. Twenty-three easing curves times three
  properties is sixty-nine individually named, individually filterable cases
  from three test methods.
- Tier 2 boxes hold. Eleven boxed tier 2 tests across two mods, including ones
  that spawn NPCs and kill town NPCs world-wide, with no cross-contamination
  and nothing needing a retry.
- Tier 3 joins a real client in 26 seconds, and both real net tests pass,
  including the tile round trip.
- The whole InnoVault suite, 112 tests over three tiers, runs in a few
  seconds at `--speed max` on top of about eight seconds of world setup.

## A seventh subject, added later

[Doze's Testing Efficiency](https://github.com/Doze-Zoze/TestingEfficiency)
branch `1.4.5`, which is not from the Workshop survey above: it was named
directly, after the fact. A mod for measuring how well a weapon performs,
about 4,400 lines, recording damage against bosses and reporting it.

**As found it builds and does not load**, and the gap between those two is the
finding. The repository carries two project files in one folder,
`TestingEfficiency.csproj` on a third-party SDK and
`TestingEfficiency-main.csproj` on the stock one, so `dotnet build` with no
argument stops at `MSB1011` and asks which. Naming the stock one builds it
cleanly, 0 errors and 13 warnings, and packs a `.tmod`. That `.tmod` then
refuses to load: *Mod name TestingEfficiency does not match assembly name
TestingEfficiency-main*, because an assembly name defaults to its project's
file name and tModLoader requires the two to agree. Building with
`-p:AssemblyName=TestingEfficiency` is the whole fix, and the mod loads.

**The suite is 55 tests**, 25 over the ID sets and 30 over the pair of string
properties a boss result is edited through. 54 pass. The one that does not is
a real defect, and it is the reason the pair is worth testing at all:

```
FormatException: The input string '' was not in a correct format.
```

`BossTestData` has two properties of the same shape, each rendering a stored
number as text and parsing it back. `timeString` parses with `int.TryParse`
and leaves the value alone when the text is not a duration. `diedString`
parses with `Single.Parse`, which throws. Their getters both render an unset
value as the empty string, so for `diedString` alone, reading the property and
writing it straight back is an exception:

```csharp
data.diedString = data.diedString;   // FormatException when died is null
```

That is exactly what a text field does, and exactly what the interface already
does to the sibling property: `SetContents(testData.timeString)` on the way in,
`testData.timeString = _` on the way out. `diedString` is not wired to anything
yet, so the defect is latent rather than live; it fires the first time anyone
gives it the same two lines its sibling has.

Two smaller observations from the same suite, both passing and both worth
saying. `died` maps zero to null, so a boss finished at exactly zero percent
and a boss never recorded are the same stored value. And the redirection set
is clean in a way worth keeping clean: nothing in `NpcToCountAs` points at
something that is itself redirected, which matters because the tracker reads
that set once rather than following a chain.

### What it taught us

**A build is not a load, and only one of the two was being checked.** The mod
compiled, packed, and produced an artifact indistinguishable from a good one.
Had the gate stopped at `dotnet build` it would have reported success over a
mod that cannot run. What caught it was the preflight from section 8.6e, which
refuses a run when a mod it was told to test is not among the loaded ones, and
which named the missing mod and listed what did load. That check was written
after a mod threw during its load pass and the suite aimed at it reported a
clean run; this is the second time it has earned its place, against a different
cause.

**Our own harness edits a file outside its scratch directory.** `ModCompile`
writes `<SavePathShared>/ModSources/tModLoader.targets` pointing at the
tModLoader that is running, and every mod build does it. The gates take care
to run the game in a scratch save directory, but a mod build is `dotnet build`
invoking tModLoader separately, which does not inherit that, so the file it
rewrites is the real one. Running the gates against a scratch install
therefore repoints the developer's `ModSources` at a directory that is about
to be deleted, and every mod there that imports `..\tModLoader.targets` stops
building until something rewrites it again. Found by walking into it: the file
pointed at a temporary tree from the previous section's work. This is the same
class as the `Mods` folder being common ground, and worse, because the path it
leaves behind does not exist.

## Reproducing

The clones are in `mods/others/`, each on a `testaria-tests` branch. The
suites need only a Testaria checkout to point at:

```
export TESTARIA_PATH=/path/to/this/checkout
dotnet build mods/others/InnoVault/InnoVaultTests/InnoVaultTests.csproj
```

`TML_PATH` is not required for the suites themselves, since the scripts and the tool ask Steam for its library folders, but `build/Testaria.props` probes a fixed list instead, so an install in a second library needs it set for an out-of-repo suite's own build.

Worth knowing when that happens, because the message does not help: the props
file carries a target that explains exactly this, and `BeforeTargets="Build"`
runs after Build's dependencies rather than before them, so the compile fails
first and what reaches the terminal is a wall of `CS0246` about `Terraria` and
`Testaria` not existing. The cause is the install, not the code.

**TestingEfficiency** needs two things its branch now carries rather than
leaving to a workaround: `AssemblyName` set on `TestingEfficiency-main.csproj`,
without which the `.tmod` builds and will not load, and the suite directory
excluded from both project files' compile globs. Its repository holds two
project files in one folder, so a bare `dotnet build` there stops and asks
which; the suite names the stock one explicitly.

Three of the mods need a local workaround that is not a source change, and so
is not committed to their branches:

- **DAYBREAK** builds through `Tomat.Terraria.ModLoader.Sdk`, which finds
  tModLoader only in Steam's primary library. Build with `-p:TmlVersion=steam`
  under a `HOME` whose primary Steam library holds a symlink to the real
  install.
- **SilkyUI** needs `SilkyUIAnalyzer` checked out at `827450e` as a sibling
  directory. Its own packaging step, `Solaestas.tModLoader.ModBuilder`, runs a
  Windows XNA shader compiler that dies under wine-mono, and computes the
  Windows save path on Linux; the build here sidesteps both by pre-creating the
  compiled-shader outputs so the step skips as up to date, and by copying the
  `.tmod` into the real `Mods` directory afterwards. Its shaders are therefore
  not real, which is fine for a build-and-load check on a server that never
  renders and not fine for anything else.
- **Quality of Terraria** is not buildable as things stand: it wants SilkyUI's
  `1.4.5-preview` branch while its sibling here is on `1.4.5`.
