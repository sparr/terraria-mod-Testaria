# Calibrating against the 1.4.5 ecosystem

Testaria pointed at every published mod with active tModLoader 1.4.5 work,
rather than at ExampleMod and its own self-tests.

Run on 2026-09-24 against tModLoader `1.4.5.8+9999.0|2026.07|1.4.5|dev`,
commit `39e7995f`, built the same day and the newest dev build available.

## The corpus

Six repositories, taken from a survey of the fifty most-subscribed mods on
the Workshop. Ranks 1 to 10 have no 1.4.5 work at all; these six are the
whole of it.

| Mod | Branch | Builds | Loads | Notes |
|---|---|---|---|---|
| InnoVault | `tml145` | yes | yes | Clean, first try. The one subject that needed nothing. |
| DAYBREAK | `1.4.5` | yes\* | after a one-line fix | `\*` its SDK cannot find tModLoader in a second Steam library |
| Cheat Sheet | `1.4.5` | no | — | 6 errors: the `WorldItem` split, `NewItem`, `FocusHelper` |
| SilkyUI | `1.4.5` | no | — | 18 errors: `FocusHelper`, plus its own source generator emitting nothing |
| Quality of Terraria | `1.4.5` | no | — | Blocked behind SilkyUI, which it project-references |
| Fargo's Mutant Mod | `1.4.5` | no | — | 23 errors: real porting work, `WorldItem` and `NewItem` throughout |

So: **two of six could be tested at all**, and one of those two needed a
source fix first. That is the ecosystem Testaria is aiming at, and it is
worth knowing before concluding anything from a corpus of one mod plus
ExampleMod.

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

- **InnoVault**, 110 tests. Tier 1 covers the easing curves and the geometry
  and byte helpers in `VaultUtils`, parameterised so each curve is its own
  reported case, plus the type registries. Tier 2 covers the TileProcessor
  lifecycle in a leased box: attach, initialise once, tick once per tick, die
  with the tile, stop ticking once dead. Tier 3 covers what a connected client
  can be asked about. 107 pass and 3 skip without a client; with a client
  attached, 109 pass and 1 skips deliberately.
- **DAYBREAK**, 16 tests. Tier 1 covers the shape of the NPC ID sets. Tier 2
  covers the IL edit behind `VulnerableToAfterPartyOfDoom`, with a control:
  the same NPC in the same routine dies when the set says nothing and lives
  when the set says no. All 16 pass.

Writing them is where the findings below came from.

## Findings

### 2. Tier 3 can only ask the client about vanilla state

`ClientLink` offers `Ping`, `AskTile`, `AskNpc`, `SendSection` and
`AwaitSection`. All of them are about state the game already owns. There is
no way to evaluate a mod-defined question on the client and get the answer
back.

For InnoVault that rules out the entire reason the mod has netcode:
TileProcessor replication, the `VaultNetworks` packet layer, and `SyncVar`
fields. The suite says so in a deliberate skip rather than quietly omitting
it, which is the honest option but not a useful one. The same limit applies
to any mod with custom packets, which is most mods with multiplayer support.

What would fix it: let a test mod register a named query that runs on the
client and returns a small payload, with the server-side `Ask` naming it.
The transport already exists; only the extension point is missing.

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
- The whole InnoVault suite, 110 tests over three tiers, runs in a few
  seconds at `--speed max` on top of about eight seconds of world setup.

## Reproducing

The clones are in `mods/others/`, each on a `testaria-tests` branch where
one exists. The two suites need:

```
export TESTARIA_PATH=/path/to/this/checkout
export TML_PATH=/path/to/tModLoader        # for an install outside Steam's primary library
dotnet build mods/others/InnoVault/InnoVaultTests/InnoVaultTests.csproj
```

Daybreak additionally needs its SDK to find tModLoader, which on a
multi-library Steam install means building with `-p:TmlVersion=steam` under a
`HOME` whose primary Steam library has a symlink to the real install.
