# Proposed sweeping invariants

Candidates for the sweeping checks, found by reading tModLoader's own
loaders and Terraria 1.4.5.8's decompiled source rather than by imagining
what a mod might get wrong. Every claim below cites where the behavior
lives.

Nothing here is implemented yet. The ordering is by value, and the last
section lists what is deliberately *not* proposed, because the loader
already refuses it.

## What makes an invariant sweepable

The three property checks and the content invariants share a shape, and
it is worth naming before adding to them, because it is what decides
whether a candidate belongs here or in a mod's own suite.

1. **It needs a subject and no understanding of one.** "Every NPC has
   more than no life" is true of anybody's NPC. "ExampleMod registers a
   mount" is not an invariant, it is a fact about one mod.
2. **Its failure is a defect rather than a choice.** A mod may
   legitimately have an odd value; it may not legitimately corrupt a
   save. Where a failure can be intended, `SweepExemptions` is the
   escape, and the mod that knows declares it.
3. **The loader does not already refuse it.** A check for something that
   throws during load can never fail, because the run never happens.
4. **It can say which subject failed.** One case per subject, named, so
   the first failure does not hide the rest.

A fifth criterion is worth adding for the ones below: prefer conditions
whose real-world failure is *silent or remote*. A mod that crashes on
load gets fixed the same day. A mod that corrupts a world on the next
version bump, or that throws only on a joining client, is exactly what a
suite is for.

## 0. Every round trip is three checks, not one

The property sweep already has the right shape, and `SweepCheck` already
names it: `GetterReads`, `RoundTrip`, `Settling`. Reading works; reading and
writing back works; writing back twice changes nothing the second time. The
`diedString` finding came out of the middle rung, and it would have been
invisible to either of the others.

Every round trip proposed below gets the same three rungs, because each one
fails differently and a single combined check would report the wrong cause:

| Rung | Asks | The failure it alone catches |
|---|---|---|
| **Reads** | the outbound half works at all | a getter, `SaveData`, or `NetSend` that throws |
| **RoundTrip** | outbound then inbound works | an inbound half that cannot consume its own outbound half |
| **Settling** | outbound, inbound, outbound again is identical | an inbound half that *silently loses or alters* state |

The third rung is the one worth insisting on, and the reason is the same in
every family. A reader that quietly drops a field does not throw and does not
fail the middle rung: the bytes are all consumed, the tag is all read, the
JSON all populates. What is gone is the state. The only thing that reveals it
is serializing a second time and finding a different answer.

So each family below is stated as a ladder. The rungs share a subject and a
case name, and they are separate results, so a report says which rung broke.
Exemptions are per rung, exactly as `SweepExemptions` already keys on
`(subject, check)`.

## 1. Net symmetry

**The strongest family here, and the one nothing else can catch cheaply.**

A mod writes entity state with `SendExtraAI` and reads it with
`ReceiveExtraAI`. If the two disagree by a single byte, tModLoader
notices, but only on the receiving machine, at runtime, in multiplayer:

```
IOException: Read underflow 4 of 12 bytes in ReceiveExtraAI
```

`NPCLoader.ReceiveExtraAI` (`NPCLoader.cs:403-441`) checks both the
compressed bit count and the byte position, logs the error, and swallows
it. So the NPC silently carries wrong state on every client for the rest
of the session. `ProjectileLoader` has the same pair
(`ProjectileLoader.cs:195-240`), and `ModSystem.NetSend`/`NetReceive` go
through `BinaryIO.SafeRead` (`IO/BinaryIO.cs:87-94`), which throws the
same underflow.

The asymmetry is trivially reachable: an `if` in the writer that is not
mirrored in the reader, a field added to one side, a `ReadInt32` against
a `Write((short))`.

A round trip needs no knowledge of the subject at all, which is what
puts it here rather than in a mod's suite:

```csharp
byte[] written = NPCLoader.WriteExtraAI(sample) ?? [];
NPCLoader.ReceiveExtraAI(sample, written);   // throws or logs on mismatch
```

`WriteExtraAI` and `ReceiveExtraAI` are both public. The one wrinkle is
that `ReceiveExtraAI` logs rather than throws, so the check has to
either drive the reader directly or watch the log. Driving it directly
is cleaner: write to a buffer, read it back with the same `BitReader`
and `BinaryReader` construction, and assert the stream is fully
consumed.

### Where the payload comes from

This is the part of the proposal that needs care, because the obvious answer
is wrong twice over.

**Not from `ContentSamples`.** `ReceiveExtraAI` *writes into the entity*. The
`ContentSamples` dictionaries hold the canonical instance of every NPC and
projectile that all inspection code reads, so round tripping on one would
corrupt shared state for the rest of the session. The subject must be a
private throwaway:

```csharp
var npc = new NPC();
npc.SetDefaults(type);      // gets its own ModNPC: NPCLoader.cs:222
                            // npc.ModNPC = GetNPC(npc.type).NewInstance(npc)
```

`NPCLoader.cs:222` assigns a fresh `ModNPC` per entity via `NewInstance`, so a
locally constructed NPC is safe to write into and the registered singleton is
untouched. Same for `Projectile.SetDefaults`.

**Default state exercises only the default branch.** A freshly defaulted
entity has `ai[]` all zero and every mod field at its initial value, so any
writer with a conditional in it takes one path and the test says nothing about
the others. That is a real limit, not a fatal one: the most common asymmetry
is unconditional (a field added to one side, a width mismatch), and that is
exactly what default state catches. But the family has to be honest that it
covers the default path, and it wants a tier 2 companion that round trips
*after* the entity has been spawned and ticked, where the state is real.

Three sources of a subject, in increasing cost and coverage:

1. A defaulted entity, tier 1. Cheap, catches unconditional asymmetry.
2. A defaulted entity with fields driven off their defaults by reflection,
   tier 1. The same no-knowledge trick the property sweep already uses, and it
   reaches conditional branches keyed on mod state.
3. An entity spawned in a box and ticked, tier 2. Real state, real cost.

Start at 1, because it is the whole of the cheap win.

### The ladder

- **1a (Reads).** `WriteExtraAI` does not throw, for every `ModNPC`,
  `ModProjectile`, and `ModSystem`.
- **1b (RoundTrip).** Writing then reading consumes exactly the bytes written,
  with no underflow of either the `BitWriter` bits or the byte stream.
- **1c (Settling).** Write, read, write again produces a byte-identical
  payload. This is the rung that matters most here and the one a naive
  symmetry check omits: a reader that consumes the right number of bytes and
  assigns them to the wrong fields, or drops one, passes 1b and fails 1c. The
  consequence is a client that disagrees with the server forever, with nothing
  in any log.

Worth knowing: this is a tier 1 check even though it is about netcode.
It needs no client, no world, and no second process, because the
asymmetry is in the mod's own two methods. Tier 3 proves the packet
arrives; tier 1 can prove the two halves agree, and should, because it
costs nothing.

## 2. Persistence robustness

`ModSystem.LoadWorldData`'s own documentation states the invariant and
nothing enforces it (`ModSystem.cs:331-336`):

> **Try to write defensive loading code that won't crash if something's
> missing.**

What happens when it is not defensive is not a caught warning. In
`WorldIO.LoadModData` (`IO/WorldIO.cs:541-557`) a throw becomes a
`CustomModDataException` that propagates out of the world load, so **the
world stops opening.** `PlayerIO.LoadModData` (`IO/PlayerIO.cs:246-272`)
does the same for `ModPlayer.LoadData`, and the comment on the save side
says so outright: *"Unlike LoadData, we don't throw error because we
don't want users to lose game progress."*

Both are called only when a tag was stored, so the case being modeled is
not "mod newly added" but the more common one: **a world or player saved
by an older version of the mod, whose tag is missing the keys the new
version reads.** `TagCompound.Get<T>` returns a default for a missing
key rather than throwing (`IO/TagCompound.cs:30-43`), so what actually
breaks is the code after the `Get`: an index, a `First()`, a `Parse`, a
non-null assumption, or a custom `TagSerializable` deserializing null.

Stated as the ladder, with the empty-tag case as a fourth rung of its own
because it is a different question from the round trip:

- **2a (Reads).** `SaveWorldData` and `SaveData` do not throw.
- **2b (RoundTrip).** Save then load does not throw.
- **2c (Settling).** Save, load, and save again produces an equal tag. A mod
  whose stored state drifts on every world save fails here and nowhere else.
  For `SaveWorldData` a throw is caught anyway (`IO/WorldIO.cs:516-526`) and
  the mod's entire world data is discarded with `saveData = new TagCompound()`,
  so 2a is about silent data loss rather than a crash.
- **2d (Absent keys).** `LoadWorldData(new TagCompound())` and
  `LoadData(new TagCompound())` do not throw. Not a rung of the round trip:
  it asks whether the inbound half tolerates input the outbound half would
  never produce, which is the documented requirement and the version-skew
  case.

2c is the most interesting of the four and the most likely to find
something, because it exercises both halves against each other rather
than asking only that neither crashes.

## 3. ID references resolve

Most of Terraria's content fields hold an integer ID indexing an array
sized to the loaded count. A field pointing past the end is an
`IndexOutOfRangeException` at use time, often on a remote client, and
often only for one code path nobody tried.

Every sentinel was checked against the decompiled field declarations in
`Item.cs`, because the check is only sound if "unset" is known exactly:

| Field | Unset | Bound |
|---|---|---|
| `createTile` | `-1` (`Item.cs:169`) | `TileLoader.TileCount` |
| `createWall` | `-1` (`Item.cs:171`) | `WallLoader.WallCount` |
| `headSlot`, `bodySlot`, `legSlot` | `-1` (`Item.cs:205`) | `TextureAssets.ArmorHead/ArmorBody/ArmorLeg.Length` |
| `ammo`, `useAmmo` | `AmmoID.None` (`Item.cs:249,253`) | `ItemLoader.ItemCount` |
| `shoot` | `0` (`Item.cs:245`) | `ProjectileLoader.ProjectileCount` |
| `buffType` | `0` (`Item.cs:279`) | `BuffLoader.BuffCount` |
| `makeNPC` | `0` (`Item.cs:127`) | `NPCLoader.NPCCount` |
| `useStyle` | `0` (`Item.cs:147`) | `ItemLoader.UseStyleCount` |
| `rare` | `0` (`Item.cs:243`) | `RarityLoader.RarityCount`, and negatives down to `-13` are vanilla specials |

Every count is a public property
(`ItemLoader.cs:31-32`, `NPCLoader.cs:30`, `ProjectileLoader.cs:25`,
`TileLoader.cs:116`, `WallLoader.cs:60`, `BuffLoader.cs:48`,
`RarityLoader.cs:10`), so this is all readable at tier 1 from
`ContentSamples`.

The equip slots are the one exception and are worth spelling out, because
the obvious route is wrong. `EquipLoader.nextEquip` is internal, and
`EquipLoader.GetEquipTexture` is public but consults a dictionary holding
only *modded* textures (`EquipLoader.cs:22, 53-56`), so it returns null
for every vanilla slot and cannot serve as a bound. The public bound is
the texture array itself, which is resized to the same count
(`EquipLoader.cs:62-67`).

- **3a.** Every item's ID-valued fields are either their sentinel or
  within the loaded count for what they name.
- **3b.** Every NPC's `banner` and `bannerItem` agree. tModLoader
  computes this exact condition and only warns
  (`NPCLoader.cs:126-133`), which makes it a ready-made invariant: the
  condition is already written, it just is not asserted.
- **3c.** Every projectile and NPC has a `ContentSamples` entry that
  agrees with it, mirroring the item check that already exists.
  `ContentSamples.ProjectilesByType` exists
  (`ID/ContentSamples.cs:815`) and is currently unused by the suite.

## 4. Clone safety

`ModType.IsCloneable` (`ModType.cs:102`) and `GlobalType.IsCloneable`
(`GlobalType.cs:96`) are public, and tModLoader computes them for every
piece of content and then only *warns*
(`ModItem.cs:59-64`, `ModType.cs:117`, `GlobalType.cs:140`):

> `X` has reference fields (`...`) that may not be safe to share between
> clones.

`CloneByReference` is tModLoader's, not ours and not a language or BCL
attribute: `Terraria.ModLoader.CloneByReference`, declared in
`patches/tModLoader/Terraria/ModLoader/CloneByReference.cs`, applicable to a
field, a property, or a whole class. So the opt-out this check points modders
at already exists upstream and needs nothing from us.

A `ModItem` that is not cloneable has items in the world sharing mutable
state. Two of the "same" sword hold one list between them; splitting a
stack splits nothing. This is a duplication and shared-state bug class
whose symptoms appear nowhere near its cause, and the check is a single
public property read.

- **4a.** Every `ModItem`, `ModNPC`, `ModProjectile`, and `GlobalX` is
  cloneable.

This one belongs with the content invariants rather than the property
sweep, because it reads the loader's own registered instances and
`TypeSweep` deliberately excludes those. Reading `IsCloneable` is safe
on a live instance: it computes over the type, and writes nothing.

Expect findings, and expect some of them to be intended: a mod may
genuinely want shared state and should say so with `[CloneByReference]`.
Where it will not, `SweepExemptions` is the right answer.

## 5. Config integrity

A `ModConfig` is serialized, cloned, and repopulated constantly:
on load, on the config UI closing, and on **every multiplayer join**
(`Config/ConfigManager.cs:203, 322, 356-378`). Two failures follow from
a config that does not survive that, and both are public API away from
being testable, since `ConfigManager.serializerSettings` and
`ConfigManager.GeneratePopulatedClone` are both public
(`ConfigManager.cs:31, 426-432`).

- **5a (Reads).** Serializing the config does not throw.
- **5b (RoundTrip).** Serializing, then populating a clone from that JSON,
  does not throw.
- **5c (Settling).** Serializing the clone produces JSON identical to the
  first. A config that fails this silently reverts the player's settings, and
  because `NeedsReload` is compared against exactly such a clone
  (`ConfigManager.cs:280-282`), it can report that it needs a reload forever,
  which is a mod that reloads on every server join.
- **5d (Absent keys).** `JsonConvert.PopulateObject("{}", config, serializerSettings)`
  does not throw. This is not a hypothetical: tModLoader does precisely
  this when a config file fails to load (`ConfigManager.cs:214-216`),
  right after warning that the file was probably corrupted. A config
  that throws here turns a corrupt settings file into a mod that cannot
  load.

5d is the config analogue of 2d, and for the same reason: the recovery
path is the one nobody tests.

## 6. Localization completeness

The suite checks three display names for the `Mods.` prefix. The same
failure reaches players through tooltips, map entries, town NPC mood and
dialogue, config labels and tooltips, buff descriptions, and death
messages, none of which are covered.

`Language.FindAll(Regex)` is public (`Localization/Language.cs:55`), and
an unregistered key is registered with its own key as its value
(`LanguageManager.cs:443-447`), so `value == key` is the exact signature
of "unfilled" rather than a heuristic.

- **6a.** No key matching `^Mods\.<ModName>\.` renders as its own key.

This subsumes the three existing checks and covers every localized
string a mod ships, from one case source, without naming any content
kind. It is also the cheapest check proposed here: one regex scan per
mod.

Some keys are legitimately blank or structural, so the check is "does not
render as its own key" rather than "is not empty".

### An `en-US`-only mod must not fail, and will not

Worth settling explicitly, because the naive form of this check would punish
the commonest and most correct case. It does not, and the reason is in how mod
localization is registered rather than in anything the check does.

`LocalizationLoader.Autoload` (`LocalizationLoader.cs:18-33`) reads **only**
the mod's `GameCulture.DefaultCulture` file, which is `en-US`, and calls
`lang.GetOrRegister(key)` for each key. `LoadModTranslations(culture)`
(`LocalizationLoader.cs:34-42`) then walks each culture and calls
`UpdateTextValue`, whose own comment states the constraint: *"can only set the
value of existing keys. Cannot register new keys."*

On top of that, English is always loaded as a base layer.
`LanguageManager.ReloadLanguage` (`LanguageManager.cs:102-117`) resets every
value to its key, loads `_fallbackCulture` first when the target differs, and
only then overlays the target; `_fallbackCulture` is
`GameCulture.DefaultCulture` (`LanguageManager.cs:31`).

So a mod shipping only `en-US` has every key registered and every value filled
in from English, whatever the active culture. A key renders as itself only when
it is missing from English *and* from the active culture, which is the defect
worth reporting. **The check is therefore locale-independent and needs no
pinning to `en-US`.**

The corollary is that this check cannot and should not measure translation
completeness. A key present in English and absent from German renders as
English, which is correct behavior, not a finding.

### 6b. A translation with no English key is dead

The registration order above has a consequence worth its own check.
Because keys are registered from `en-US` alone and `UpdateTextValue` cannot
create new ones, **a key that appears in a translated file but not in
`en-US` is silently discarded.** The translator's work is simply never
reachable, with no warning at load and no way to notice in play.

- **6b.** Every key in a mod's non-`en-US` localization files also exists in
  its `en-US` files.

Checkable offline from the `.hjson` files, or at runtime with
`Language.Exists`. Yield on the current corpus: **zero across 69 translated
files**, so this is a guard rather than a discovery. Recorded because the
failure is invisible by construction and the check is nearly free.

One implementation note, learned the hard way: several of the corpus's
translated files begin with a UTF-8 BOM. A first pass at this scan read them
without stripping it, mis-nested the first key of every such file, and
reported four dead keys in Fargowiltas that do not exist. Read localization
files as `utf-8-sig`.

## 7. Placeability

`Main.tileFrameImportant[type]` without a registered `TileObjectData`
makes a tile that cannot be placed at all: `TileObject.CanPlace` fetches
the data and returns false when it is null
(`TileObject.cs:176-181`). The item exists, the tile exists, and
clicking does nothing.

- **7a.** Every `ModTile` with `Main.tileFrameImportant` set has
  `TileObjectData.GetTileData(Type, 0)` return non-null.
- **7b.** Every item with `createTile >= 0` names a tile that can be
  placed, which is 7a reached from the other side.

## 8. Tier 2 candidates

These need a world and are not cheap, so they belong behind an opt-in
rather than in the default sweep. They are listed because they catch a
class the tier 1 checks structurally cannot: a mod whose content is
well-formed and whose behavior throws.

- **8a.** Every `ModNPC` spawned in a box and ticked for a few hundred
  ticks does not throw. An AI indexing `ai[]` it never initialized, or
  assuming a target exists, fails on the first frame.
- **8b.** Every `ModProjectile` spawned and ticked does not throw, and
  expires within its own `timeLeft`. A projectile that never dies leaks
  the 1000-slot pool until nothing in the world can shoot anything.
- **8c.** Every `ModTile` placed and then mined does not throw and
  leaves no tile behind.

8b is the most valuable of the three, because pool exhaustion presents
as "the game stopped working" with no exception anywhere.

## 9. Weaker candidates

Listed with their problems rather than left out, since the reason a
check is not worth adding is worth recording too.

- **9a. Static arrays sized to a vanilla `Count`.** A mod's own
  `new bool[ItemID.Count]` indexed by a modded ID throws. Detectable by
  reflecting over static array fields whose length equals a vanilla
  count while the loaded count is larger. The problem is that a
  vanilla-only array is legitimate, so this produces false positives,
  and tModLoader has already closed most of this class: `SetFactory`
  now throws if a named set is created during `Load`
  (`ID/SetFactory.TML.cs`, `RegisterNamedCustomSet`). Report, never
  gate, if at all.

  **Superseded.** Surveying the corpus promoted this to a real family,
  3d to 3g, because two mod suites already hand-write it and one of them
  gets the bound wrong. See "ID sets: the family I missed" below; read
  that instead of this.
- **9b. A recipe that duplicates its own ingredient.** One ingredient,
  same type as the result, result stack at least the ingredient stack,
  is a free-money loop. Precise and needs no knowledge, but it is an
  economy exploit rather than a crash, which is a different claim than
  the rest of the suite makes.

  **Implemented, behind `ECONOMY=1`.** That different claim is the reason for the
  switch rather than any cost; see "9b, and a rule the core can test" below.
- **9c. A town NPC has a head texture.** Head textures autoload only
  under `[AutoloadHead]` (`ModNPC.cs:127-129`). Without one the map
  entry degrades rather than crashing, so this is a quality check.

  **Not ours: an upstream analyzer, not started.** A missing attribute is a
  syntactic fact, and the argument that sent 6b and 9a to `tModCodeAssist` sends
  this too. The rule would be: in a type deriving from `ModNPC` that assigns
  `NPC.townNPC = true`, require `[AutoloadHead]` on the class. What it has to get
  right is the legitimate alternative, `Mod.AddNPCHeadTexture` called by hand,
  which an analyzer can see in the same compilation but has to go looking for.
  Reporting a mod that registers its head the other way would be the check's
  ignorance rather than the mod's defect, which is the mistake the withdrawn
  placeability checks made once already.

## What the loader already refuses

These would be reasonable invariants and are not worth writing, because
a mod that violates them never finishes loading, so the check could
never fail. Recorded so nobody adds them later.

- **Recipes are thoroughly validated at registration**
  (`Recipe.TML.cs:105, 137, 152, 183, 201, 456, 459`): a nonexistent
  ingredient item, a nonexistent recipe group, a nonexistent required
  tile, a recipe with no result, and registering the same recipe twice
  all throw. Recipe invariants are close to fully covered already, which
  is why only the duplication loop in 9b survives.
- **`SaveData` and `LoadData` must be overridden together**
  (`ModPlayer.cs:49`, `ModSystem.cs:27`), as must `CopyClientState` and
  `SendClientChanges` (`ModPlayer.cs:50`).
- **Custom ID sets cannot be created during `Load`**
  (`ID/SetFactory.TML.cs`), which is what guarantees they are sized to
  the final content count.
- **`SetFactory` names must be unique**, and named sets must agree on
  their default value and length across mods.

One near-miss worth stating, because it looks redundant and is not: the
existing `An_item_has_a_stack_limit_of_at_least_one` is not covered by
the loader. `ItemLoader.ValidateDropsSet` (`ItemLoader.cs:150-168`) does
validate `minStack` and `maxStack`, but only for entries in
`ItemID.Sets.GeodeDrops` and `ItemID.Sets.OreDropsFromSlime`, not for
items in general.

## Suggested order

**Superseded by "Revised order" at the end**, which accounts for what the
corpus survey found. Its numbering also predates the ladder restructure in
section 0, so read `2d` here as the settling rung and `5b` as the absent-keys
rung. Kept because the reasoning differs: this ordering was
derived from the game and the loader alone, before any mod was read.

If these land incrementally, the order that gets the most out of the
least work:

1. **6a**, localization. One regex per mod, subsumes three existing
   checks, and covers every localized string.
2. **4a**, cloneability. A public property read, and tModLoader has
   already written the condition.
3. **2a, 2b, 2c**, persistence robustness. Three assertions, and the
   failure they catch is a world or player that stops loading.
4. **1a, 1b, 1c**, net symmetry. The highest-value family, and the most
   implementation work, because the round trip has to be driven by hand.
5. **3a, 3b**, ID references. Mechanical, but the table of sentinels has
   to be right or it produces noise.
6. **5a, 5b**, config integrity. Few mods will have many configs, so
   the case count is small, but the failure is a reload loop on join.
7. **2d**, save round trip settling. Last of the tier 1 work because it
   is the most likely to need per-mod exemptions.
8. **7a, 7b**, then the tier 2 family behind an opt-in.

# The corpus, surveyed

The sections above came from reading the game and the loader. This one comes
from reading the mods, which is the only way to find out whether a proposed
invariant would ever fire, and the only way to find the ones nobody thought
of.

Eight mods contributed cases to the recorded sweep. Seven are present as
source under `mods/others/`, ExampleMod under `tModLoader/`, and three
(`BeardBench`, `DragonLens`, `RecipeBrowser`) only as installed `.tmod`
files. Source totals, excluding `obj/`, `bin/`, and worktrees:

| Mod | Files | Lines | Has a suite |
|---|---|---|---|
| InnoVault | 678 | 146,678 | yes |
| daybreak-mod | 286 | 110,456 | yes |
| ImproveGame | 564 | 76,218 | no |
| Fargowiltas | 355 | 36,074 | no |
| TestingEfficiency | 168 | 28,289 | yes |
| SilkyUIFramework | 117 | 12,197 | yes |
| CheatSheet | 59 | 11,742 | yes |

`ImproveGame` is the mod the calibration notes record as unbuildable, so
anything found in it is unverifiable by running and is marked as such.

## Ground truth from the recorded run

The archived log of the ten-mod sweep is the authoritative answer to "what
does the loader already complain about in this corpus". Extracted from
`tModLoader-Logs/Old/2026-09-26-47.zip`:

- **Zero `ERROR` lines.** The corpus loads clean.
- **One cloneability warning**, discussed below.
- **226 `Silently Caught Exception` warnings, 173 of them
  `Testaria.Assert.Skip`.** This is a finding about Testaria, not about any
  mod: `Assert.Skip` throws, and tModLoader's exception logging catches and
  records every throw, so a skip-heavy sweep writes hundreds of scary-looking
  entries into the game log. `An_item_that_deals_damage_can_be_used` alone
  accounts for 135. Worth fixing separately, because it makes the game log
  useless for exactly the audience most likely to read it.

That is the calibration for every proposal above: in this corpus the loader
finds nothing, which is why the checks have to look at things the loader does
not.

## What the corpus did to the proposals

### Net symmetry is corroborated, and gains a limit

**Thirty `NetSend`/`NetReceive` and `SendExtraAI`/`ReceiveExtraAI` override
pairs across five mods.** Every pair read by hand is symmetric, including the
two least obvious, so this family would currently pass. Three things came out
of reading them anyway.

**InnoVault independently built the defense this invariant is about.**
`InnoVault/GameSystem/NPCRebuildLoader.cs:362-440` wraps each sub-override's
payload in a `ushort` length prefix, seeks back to fill it in, and on the
receiving side compares `stream.Position` against the recorded end, logging
when they disagree. Its own comment states the consequence precisely: a
mismatch misaligns "the same NPC's subsequent overrides and even other mods'
ExtraAI stream". A framework mod arriving at the same conclusion, and at the
same detection mechanism, is the strongest evidence available that this is a
real class rather than a theoretical one. It also means InnoVault's
sub-`NetSend`/`NetReceive` pairs are a second, finer-grained surface a suite
could assert directly instead of waiting for the log.

**The check must measure bytes, not calls.** `ImproveGame`'s
`IndicatorMapLayer` writes `(short)Main.dungeonX` and `(short)(Main.dungeonY
- 6)` as two calls and reads them back as a single `ReadPoint16()`. Seventeen
writes against sixteen reads, and entirely correct. A check that counted
calls would report it; a check that compares stream position will not.

**A local round trip cannot prove cross-machine symmetry.** Daybreak's
`DownedFlagHandler` (`DownedFlagHandler.cs:63-95`) opens both halves with
`if (Mod.NetID < 0) return;`. The guard is symmetric in the source and reads
*machine-local* state, so writer and reader always agree within one process
and can still disagree across two. This is a genuine limit of 1a to 1c, of
the same kind as "a box is a region": the check proves the two methods agree
about a given state, not that both machines are in that state. It should be
stated rather than papered over.

**One pair I could not settle by reading**, which is the argument for the
test rather than against it. `ImproveGame`'s `TEAutofisher`
(`TEAutofisher.cs:1129-1166`) writes items with `writer.Write(fishingPole)`,
an extension from the bundled `NetSimplified` library whose own documentation
pairs it with `ReadItem`, and reads them back with tModLoader's
`ItemIO.Receive(reader, true)`. Two libraries' formats and an explicit
`readStack: true` against an unknown writer default. Settling this needs the
DLL's IL; a round trip would answer it in milliseconds.

### Cloneability: less yield, more confidence

The corpus produced **exactly one** cloneability warning, not the dozens a
source-level grep suggests. A grep is the wrong instrument here and I should
say so plainly: mine matched local variables inside method bodies and
returned seventeen candidates, of which the loader agrees with one. The
authoritative source is the `IsCloneable` property at runtime, which is
precisely why this belongs in a test rather than in a linter.

The one real finding is `Daybreak.Common.Features.Models.ItemDataProviderImpl`,
holding a `Dictionary<Type, IBoundDataProvider>` backing field.

**And it is deliberate, declared, and already enforced by the mod itself.**
`daybreak-mod/src/Daybreak/Common/CodeAnalysis/CloneabilityContracts.cs`
defines:

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class ExpectCloneableAttribute(bool isCloneable = true) : Attribute
```

and a `ContractEnforcer` that reads `IsCloneable` reflectively at load and
throws `InvalidOperationException` when it does not match the declaration.
`ItemDataProviderImpl` and `PlayerDataProvider` both carry
`[ExpectCloneable(false)]`.

This is the same invariant as 4a, invented independently, *plus* the
exemption mechanism, and it settles the design question 4a leaves open. The
assertion should not be "every content class is cloneable". It should be
"every content class is as cloneable as it declares", defaulting to
cloneable when nothing is declared. That is `SweepExemptions` with a
different spelling, and the fact that a mod author reached for the same shape
unprompted is a good sign for both.

Daybreak's version is opt-in: `CheckCloneabilityContracts` returns early
when the attribute is absent, so it only covers types Daybreak annotated. A
sweep's version is opt-out, and therefore strictly stronger, which is the
right default for a framework that knows nothing about its subject.

### Persistence: one real finding, and a gap in the proposal

**Ninety `Save`/`Load` overrides across forty-six files.** A mechanical scan
for non-defensive patterns flagged six. Five are correct, and two are worth
keeping as the reference for what correct looks like:

- `ImproveGame/Common/ModPlayers/DataPlayer.cs:37` bounds its copy loop by
  *both* arrays: `i < SuperVault.Length && i < superVault.Length`.
- `ImproveGame/Content/Tiles/TEAutofisher.cs:1066` follows its read with
  `Array.Resize(ref fish, 40)` and a comment marking it as old-version
  compatibility.

The sixth is a real defect. `CheatSheet/CheatSheetPlayer.cs:46`:

```csharp
public override void LoadData(TagCompound tag) {
    tag.GetList<TagCompound>("ExtraAccessories").Select(ItemIO.Load).ToList().CopyTo(ExtraAccessories);
    ...
}
```

`List<T>.CopyTo(T[])` throws `ArgumentException` when the source is longer
than the destination. `ExtraAccessories` is `new Item[MaxExtraAccessories]`,
and `MaxExtraAccessories` is `public static int MaxExtraAccessories = 6`, a
mutable static rather than a constant. So any player file written while that
number was higher makes `LoadData` throw, which `PlayerIO.LoadModData`
converts into a `CustomModDataException`, and **the player file stops
loading.** It is latent rather than live, in the same way the calibration
notes describe `diedString`: it fires the first time the number moves, and
nothing stops another mod from moving it.

**Neither 2d nor 2c would catch it, and that matters.** An empty tag yields
an empty list, and `CopyTo` of nothing succeeds. A save-load-save round trip
uses the current size on both sides. The hazard is strictly cross-version,
and a single-process test cannot manufacture a previous version's tag.

So the corpus argues for one more check than I proposed:

- **2e.** `LoadData`/`LoadWorldData` survive a tag whose lists have been
  lengthened beyond what the current build would write. Take the tag the
  mod's own `SaveData` produced, duplicate the entries of every list in it,
  and load that. It is the cheapest available stand-in for "a save from a
  build that stored more", and it is exactly what finds `CopyTo`.

2e is a mutation test rather than a property, so it belongs behind the same
opt-in as the tier 2 family rather than in the default sweep. But it is the
only one of the persistence checks that would have found the corpus's one
real persistence bug, which is a strong argument for writing it.

### ID sets: the family I missed

This is the largest gain from reading the corpus, and it came from reading
the suites rather than the mods. **Two independent mod suites hand-write
invariants over ID sets**, and between them they describe a family I ranked
as a weak heuristic in section 9a.

`TestingEfficiency/TestingEfficiencyTests/IdSetTests.cs` asserts, over
`NPCID.Sets`-derived sets the mod registers:

- the set is sized for every loaded NPC, not for vanilla's count;
- every redirection names an NPC that exists;
- no redirection points at something itself redirected, because "the tracker
  reads the set once, not in a loop, so a chain would credit damage to the
  middle of it";
- nothing is redirected to itself.

`daybreak-mod/src/DaybreakTests/NpcSetTests.cs` asserts the sizing property
over its own sets, and that a deferring set has no opinion by default.

Three of those four are invariants I never proposed, and all four generalize:
they need no knowledge of what the set *means*, only that it is an array
indexed by an ID space and, where its values are themselves IDs, that they
resolve and do not chain. Promote 9a out of the weak section and restate the
family:

- **3d.** Every ID-indexed set a mod exposes has `Length` at least the loaded
  count for its ID space. Sets built through `XID.Sets.Factory` get this for
  free, because tModLoader sizes the factory to the loaded count
  (`NPCID.cs.patch:186-187` replaces `new SetFactory(Count)` with
  `new SetFactory(NPCLoader.NPCCount, ...)`). Hand-rolled arrays do not.
- **3e.** Every value in an ID-to-ID set either is the set's default or names
  a loaded ID.
- **3f.** No entry in an ID-to-ID set points at an entry that is itself
  mapped, for any set read as a single hop.
- **3g.** No entry maps to itself.

3f and 3g are cheap, need no knowledge, and catch a failure that is
otherwise invisible: a chain produces a wrong answer rather than an
exception.

#### A defect in one of the suites

Verifying 3d turned up a real one, in a suite written for this framework.
`IdSetTests.Set_is_sized_for_every_loaded_npc` documents its intent as "every
set is sized to the loaded NPC count rather than to vanilla's", and then
asserts:

```csharp
Assert.Equal(NPCID.Count, length, $"{which} is sized {length}, not the loaded NPC count");
```

`NPCID.Count` is `public static readonly short Count = 697`
(`decompiled/1.4.5.8/.../ID/NPCID.cs:12465`) and tModLoader never changes it;
the loaded count is `NPCLoader.NPCCount`. The sets under test are built
through `NPCID.Sets.Factory`, which tModLoader sizes to `NPCLoader.NPCCount`.
So the assertion compares the loaded length against vanilla's and **passes
only in a run where no enabled mod adds an NPC.** Enable it alongside
ExampleMod, which adds several, and it fails, while the set it is testing is
correct.

`Every_redirection_names_a_real_npc` has the milder form of the same problem:
it requires `target < NPCID.Count`, so a redirection aimed at a modded NPC
would be reported as invalid. Daybreak's `set.Length >= NPCID.Count` is safe
by comparison, being a lower bound that a correctly sized set always clears.

The fix is `NPCLoader.NPCCount` in both places. The general lesson is worth
carrying into the framework's own guidance, because it is the mirror of the
tier 0 trap the README already documents: **`XID.Count` is the vanilla count
at runtime too, not just outside the game**, so any assertion about "loaded"
anything must go through the loader's count.

### Registry integrity, from InnoVault

`InnoVault/InnoVaultTests/RegistrationTests.cs` tests its own content
registry for properties that generalize to any mod keeping one:

- every entry has a non-null mod and a non-blank name;
- **full names are unique.**

Uniqueness is the interesting one. tModLoader guarantees it for content it
registers, but a mod maintaining its own registry keyed by string gets no such
guarantee, and a collision silently overwrites the earlier entry.

- **10a.** Every mod-maintained registry keyed by name has unique, non-blank
  keys.

This needs a way to find such registries, which a sweep cannot do
generically, so it is better offered as a helper a mod's own suite calls than
as a swept invariant. Recording it because it is a real class, and because
one line in a mod's suite covers it.

### Config integrity is corroborated by a workaround

Fifteen `ModConfig` classes across seven mods, so this ladder has subjects.
More usefully, **CheatSheet has already hit 5c and worked around it.**
`CheatSheet/Configuration.cs:73-81`:

```
// ReferenceDefaultsPreservingResolver messed up with ServerConfiguration serialization.
// TODO: Migrate to regular ModConfig stuff, ...
json = JsonConvert.SerializeObject(serverConfiguration, new JsonSerializerSettings { ... });
```

It serializes that object with hand-rolled settings while still
*deserializing* it with `ConfigManager.serializerSettings` at line 47. An
asymmetric serializer pairing, diagnosed to tModLoader's own contract
resolver, patched rather than fixed, with a TODO admitting it. That is
proposal 5c described from the other side, by somebody who lost time to it.

The object at the center of it is also a clean example of a getter and setter
that cannot round trip by construction
(`CheatSheet/Configuration.cs:118-131`):

```csharp
public NPCDefinition[] BannedNPCs {
    get => Menus.NPCBrowser.filteredNPCSlots.Select(type => new NPCDefinition(type)).ToArray();
    set {
        // This will forget unloaded modnpc.
        List<int> loaded = value.Where(...).Select(npc => npc.Type).Distinct().ToList();
        ...
    }
}
```

The comment states the defect. The setter drops unloaded entries and
deduplicates, so what comes back out is not what went in.

### A hazard for the property sweep itself

That same property is worth one more note, because it is about the sweep
rather than about CheatSheet. Its getter and setter do not touch a backing
field: they read and write `Menus.NPCBrowser.filteredNPCSlots` and set
`needsUpdate = true`. **Writing a property back can therefore change global
mod state**, and `SettersAcceptTheirOwnGetters` writes every property it can
reach.

`ServerConfiguration` happens to be `internal`, and `TypeSweep.Constructible`
requires `type.IsPublic`, so the sweep does not reach it today. That is luck
rather than design. The existing documentation explains why live
loader-registered instances must never be written to; this is a different
case, a freshly constructed, non-loader-bound object whose setter reaches
past itself into global state. Worth stating in `TypeSweep`'s own notes,
since the next mod may spell the same thing `public`.

### Localization: the caveat was necessary

`ImproveGame` ships five `Tooltip: ""` entries in its English item
localization, deliberately, for items with no tooltip. So the "is not empty"
form of 6a would have produced five false positives against the first mod
that has any localization depth, and the "does not render as its own key"
form is not a nicety. One hundred fourteen `.hjson` files across the corpus,
so 6a has plenty to work on.

## Revised order

The corpus changes the ranking. Net symmetry stays valuable but would find
nothing today; the ID set family would find something immediately and is
cheap.

1. **3d to 3g**, ID set integrity. Two mods hand-write these already, one of
   them incorrectly. Highest confidence, immediate yield, no knowledge
   needed.
2. **6a and 6b**, localization. Cheapest checks, 6a subsumes three existing
   ones, and the corpus confirmed the correct form of both.
3. **4a**, cloneability as declared. One finding in the corpus, and Daybreak
   has already designed the declaration mechanism.
4. **2a to 2d**, the persistence ladder, then **2e** behind an opt-in. 2e
   is the one that finds the corpus's real persistence bug.
5. **1a to 1c**, net symmetry, with the cross-machine limit documented. No
   current findings, thirty subjects, and a framework mod's own defenses as
   evidence it matters.
6. **5a to 5d**, config integrity. One corroborated real-world instance.
7. **3a, 3b**, ID reference validity, then **7a, 7b** and the tier 2 family.

Two items of framework work fell out of this survey and are not invariants at
all:

- `Assert.Skip` writes 173 `Silently Caught Exception` entries into the game
  log in a single sweep run. The log is where modders look; the sweep should
  not flood it. **Done**, and measured again on the way: see "The log flood,
  and the three that survived it" below.
- `TypeSweep`'s notes should record that a setter can reach global state, not
  only that live content must not be written. **Done**: the class summary now
  says a constructed subject is not a sandbox, and `IsLoaderBound` says that
  excluding the loader's own instances protects those instances and nothing
  else.

# What is implemented

Written after the survey, in the revised order, and run against ExampleMod with
its own suite enabled. What follows is the state of each item and what it cost
to calibrate, not a plan.

| Item | Where | State |
|---|---|---|
| 1a to 1c, net symmetry ladder | `NetSweep`, `NetSymmetryTests` | swept, nothing found |
| 2a to 2d, persistence ladder | `PersistenceSweep`, `PersistenceTests` | swept, 2 findings |
| 2e, lengthened-list mutation | `PersistenceSweep`, `PersistenceTests` | swept, 1 finding |
| 3a, item ID references | `ReferenceSweep`, `ReferenceTests` | swept, nothing found |
| 3b, NPC banner agreement | `ReferenceSweep.NpcBannerAgrees` | swept, public half only |
| 3d, ID set sizing | `IdSetSweep.IsSizedForLoadedContent` | for a mod's own suite; the sweep was withdrawn |
| 3e to 3g, redirection flatness | `IdSetSweep.RedirectionIsFlat`, `SweepDeclarations` | swept where declared, tested |
| 4a, cloneable as declared | `CloneSweep`, `CloneTests` | swept, 5 findings |
| 5a to 5d, config ladder | `ConfigSweep`, `ConfigTests` | swept, 1 declared intended |
| 6a, keys are filled in | `LocalizationSweep`, `LocalizationTests` | swept, nothing found |
| 6b, unreachable translations | a tModLoader branch, not here | written as an analyzer, handed over |
| 7a, 7b, placeability | withdrawn | invalid: framed tiles place by the plain path |
| 8a to 8c, tier 2 family | `BehaviourSweep`, `BehaviourTests` | swept behind `BEHAVIOUR=1`, nothing found |
| 9a, vanilla-sized arrays | its own tModLoader branch, when somebody writes it | not started, and not ours |
| 9b, self-duplicating recipe | `RecipeShape` in the core, `RecipeSweep`, `RecipeTests` | swept behind `ECONOMY=1`, nothing found |
| 9d, free-output recipe loop | `RecipeLoops` in the core, `RecipeSweep`, `RecipeTests` | swept behind `ECONOMY=1`, nothing found |
| 9c, town NPC head texture | an upstream analyzer, when somebody writes it | not started, and not ours |

Ordered by item rather than by the order they were written, because this is now
a state table rather than a record of an afternoon. The findings column counts
the twelve-mod run at the end of this document, not the ExampleMod-only run
below; "nothing found" means the check ran against that corpus and reported
nothing, which is a different claim from "no defect exists".

### 9a is not ours to ship

The precise form of 9a needs an array's *size expression* rather than its
length, which makes it a syntactic question and therefore an analyzer's. This
repository has an analyzer project, and it is not the place for it.
`Testaria.Analyzers` exists to enforce the tier 0 boundary, `TSTA001` and
`TSTA002`, which is a claim about this framework's own tiers and means nothing
to anybody else. A rule about how a mod sizes its arrays is a general mod
authoring rule with no connection to tiers, and shipping it here would mean a
modder installing a test framework to be told about their arrays.

So it goes where 6b went: its own branch on tModLoader, against
`tModCodeAssist`, which mods already build against. Not started. What it has to
decide is recorded in `IdSetSweep`'s own notes and in section 9a above: a
vanilla-only lookup is legitimate, so the rule is about `new T[XID.Count]`
sized from a vanilla count and then indexed by something that can exceed it,
which is the part only the syntax knows.

## The log flood, and the reflection boundary under it

`TestariaSystem.Load` now calls
`Logging.IgnoreExceptionContents("Testaria.Assert.Skip")`, which is the hook
tModLoader offers for exactly this: `ignoreContents` is matched against the
stack trace the first-chance handler takes at throw time, so naming the throwing
method suppresses skips and leaves everything else alone. Not
`IgnoreExceptionSource`, which would silence a failing assertion too.

Measured on a seven-mod sweep, `Testaria TestariaSweepTest ExampleMod
CheatSheet DragonLens InnoVault Daybreak`, 2135 tests with 245 skips:

| | `Silently Caught Exception` entries | of those, skips |
|---|---|---|
| Before | 206 | 177 |
| After | 32 | 3 |

The 29 that are not skips are the same in both columns: the self-test's
deliberate throws, the eight findings, and one read of `/proc`.

**Three skips reached the log anyway, and the reason decided the second half of
the fix.** Their stacks began inside `RuntimeMethodHandle.InvokeMethod` with no
`Assert.Skip` frame: they were second events for the same exception, raised where
it crossed back out through the reflection call that invoked the test.
tModLoader's handler would have deduplicated them against `previousException`,
but it only records that after deciding to log, and the pattern returns first.

So the reflection went. `TestInvoker` compiles one expression per test method,
`(instance, arguments) => ((Suite)instance).Method((T)arguments[0], ...)`, and
`TestRunner` calls that instead of `MethodInfo.Invoke`. A compiled call is a
direct call: the body's exception propagates once, through frames that name the
test. The same sweep now logs **27 entries and no skips at all**, with 245 skips
in the run, and it stays at 27 with `ECONOMY=1` adding 137 more cases.

Two things fell out of it that were not the point. `TargetInvocationException`
entries went from 5 to 3, since the runner no longer creates the wrappers it then
unwrapped. And the stack trace recorded against a failing test is now the test's
own, without four frames of invoke machinery on top of it.

A signature the expression compiler will not bind, a by-ref parameter for
instance, returns null from `TestInvoker.For` and the runner falls back to
`MethodInfo.Invoke` for that method alone. Tier 0 covers the fallback, and covers
the thing worth checking by hand: a private method on a private type is callable
this way, which it has to be, since discovery accepts non-public test methods and
a suite inside a mod that keeps its types internal is a case the ecosystem
calibration already ran into.

`Assert.Skip` also carries `[MethodImpl(MethodImplOptions.NoInlining)]`, so the
frame the pattern names cannot be optimised away once a caller is hot. Insurance
rather than a measured fix: the count was 3 with and without it, because what the
pattern was missing then was the second event rather than an inlined frame.

## 9b, and a rule the core can test

The free-money recipe is in, behind `ECONOMY=1`, which passes
`-testariaeconomy` and is read through `TestSession.EconomyRequested`. Asked for
or not, the cases exist: `RecipeTests` skips each one with its reason when the run
did not ask, the same shape `BehaviourTests` uses, so a report can never read as
though a mod's recipes had been checked when they were not.

**The switch is about the claim, not the cost.** Reading a recipe is free. Every
other check in the suite says a mod will break; this one says a mod is
unbalanced, which is the author's business and, in a cheat or tooling mod, is
usually the point. Keeping the two apart is what lets the default report mean one
thing.

**The rule.** One ingredient, of the result's own type, and a result stack no
smaller than the ingredient's. Each condition excludes a legitimate recipe: a
second ingredient is consumed, so the loop costs something; a smaller result is a
compression recipe, which is the useful inverse; a different item is somebody
else's business. Recipe groups are followed, because that is how the loop arrives
by accident rather than by design: one group, written once and reused, that holds
both the ingredient and what the recipe makes. A disabled recipe is skipped, since
it cannot be crafted and a pass would claim an answer.

**The rule lives in the core, and that is the interesting part.** `RecipeShape.Duplicates`
is a pure function over ingredients, a result, and the set of types the recipe's
groups accept. It is there because the sweep cannot be self-tested: every sweep
excludes mods whose name ends in `Test` or `Tests`, so a deliberately broken
recipe in this repository is invisible to it, which is exactly the wall the
withdrawn 3d check hit. Ten tier 0 tests cover the rule in both directions,
including the group cases and the compression recipe it must not report. What is
left untested in the game is the plumbing around it, not the decision.

**Measured.** With the seven-mod corpus, `ECONOMY=1` gives **2272 tests, 2019
passed, 8 failed, 245 skipped**: 137 recipe cases, nothing found. Without the
switch the same 2272 tests report 382 skipped, the extra 137 being these carrying
their reason.

**All 137 belong to ExampleMod.** Cheat Sheet, DragonLens, InnoVault and Daybreak
add no recipes at all, which is what a corpus of tools and frameworks looks like:
they hand out items through their own UI rather than through crafting. So "nothing
found" here is one content mod's worth of evidence, and should be read the way the
content invariants are read rather than as a verdict on the ecosystem.

### 9d, the same defect spread over several recipes

Not from the survey: 9b asks about one recipe, and the form that survives review
is the one no single recipe is guilty of. Wood into sticks into wood, where the
round trip comes back with more wood than it started with. Each recipe is
defensible alone.

**The model.** Every recipe with exactly one ingredient slot is an edge from each
type that can satisfy it to its result, carrying the two stacks. A loop is a cycle
in that graph, and what it yields is the product of the edges' ratios, computed
exactly in `BigInteger` rather than in logs, because the case that must not be
reported is the one where the product is exactly 1. Reversible conversions are
everywhere: coins, and ExampleMod's own blocks and walls. A loop is reported only
when the product is strictly greater than 1.

Recipes with two ingredients form no edge. The second is consumed, so the loop
costs something unless that ingredient is itself free, and deciding *that* is
reachability over multisets rather than a cycle in a graph: a solver with a
timeout instead of a search with an answer. The restriction is also what makes the
arithmetic exact, since one slot in and one stack out is a ratio.

**The two kinds of free output, which is the distinction a report has to make.**
A gainful loop makes every item it passes through unlimited, and those items are
its own ingredients: more of what it consumed. Everything else that becomes free
is a different item, reached by closure: any recipe all of whose slots can be
satisfied from something already unlimited produces another unlimited item, and so
on until nothing new appears. `RecipeLoop` carries the two as `Items` and
`NewItems`, and the message names them separately, because "this loop duplicates
your ore" and "this loop makes your endgame sword free" are different sentences to
the person fixing it.

**Vanilla's recipes are in the graph, and only a mod's are cases.** A mod can
close a loop through vanilla with one recipe of its own, and that loop is the
mod's to answer for. Every recipe in a loop reports the whole loop rather than one
of them being named the culprit: each is defensible alone and any one of them can
be the edit that breaks the cycle.

**Two caps, and neither is silent.** A loop may be at most 8 recipes long, and the
search stops after 500,000 steps, since cycle enumeration is exponential in the
worst case over somebody else's data. `The_free_output_loop_search_finished` is one
case for the whole run that fails if the budget ran out, because a bounded search
that gave up looks exactly like a clean one.

**Measured on the seven-mod corpus: 138 cases, nothing found.** The search runs
once, on the first case that asks, over every loaded recipe including vanilla's,
and takes 0.64 seconds; every other case reads the cached answer in under a
millisecond.

**Nothing found is not the same as working, so it was checked from the other
side.** Relaxing the threshold from "returns more than it took" to "returns at
least what it took" made the same run report **10 loops in ExampleMod**, all of
them block and wall and platform conversions of the form 1 block to 4 walls and 4
walls back to 1 block. That is the graph built from real recipes, cycles found
through them, and the strict comparison being the only thing keeping ten
legitimate conversions out of the report. The threshold went back afterwards.

**What it does not catch.** A loop that needs a second ingredient, per the model
above. A loop that exists only through vanilla's hardcoded substitutions, since
`useWood`, `useSand`, `useIronBar`, `usePressurePlate` and `useFragment` are
private predicates on `Recipe` rather than recipe groups, and only groups are
followed. And a recipe with no ingredients at all, which `Register` permits and
which is free output without any loop: a sibling check rather than this one.

**What it does not catch**, recorded so nobody assumes otherwise: a two-ingredient
loop whose second ingredient is effectively free, a recipe whose `Condition`
restricts when it can be crafted at all, and anything about shimmer decrafting,
which is a separate table with separate rules.

## The ladder is one vocabulary, and it paid for itself immediately

Every new ladder reuses `SweepCheck`'s existing three values rather than
inventing its own, and every rung consults `SweepExemptions` through one shared
helper. That turned out to matter on the first run against ExampleMod.

ExampleMod's suite already declares one exemption, for the config property that
adds 0.2 to whatever it is given on purpose. It was written for the property
sweep. The new config ladder found the same defect by an entirely different
route, through JSON rather than through reflection:

```
written:   { "Property": 0.2 }
rewritten: { "Property": 0.4 }
```

Because both name the same subject and both ask `SweepCheck.Settling`, the one
existing declaration silenced both. No new exemption, no second list, nothing
for ExampleMod to do. That is the argument for the shared vocabulary rather
than a per-family one, and it is worth recording because the alternative looks
equally reasonable until you watch it duplicate.

## Calibrating cloneability: 16 to 2

The cloneability check needed the same narrowing the property sweep needed, for
the same reason, and the numbers are the record of it.

As first written it reported **16 failures against ExampleMod**, which was
enough to know it was wrong: the reference mod is not fifteen-sixteenths broken.
Making the message name the offending fields and their declaring assembly
answered it at once. Fifteen of the sixteen read:

```
fields the mod declares: none
fields tModLoader declares: ModNPC.<SpawnModBiomes>k__BackingField (Int32[]), ...
```

`ModNPC` declares `SpawnModBiomes` as an `int[]`, and `ModType` declares `Mod`
and `Entity`. A type is not cloneable when any class in its hierarchy has
reference fields and none overrides `Clone`, so **every `ModNPC` in every mod
reports false through no fault of its own.** Restricting the finding to fields
the subject's own assembly declares leaves 2.

This is the same statement `Assert.OwnProperties` already makes about
properties, arrived at from the other direction: these checks ask about the code
somebody wrote, not the framework it derives from. A type whose only candidate
fields belong to tModLoader is reported as a skip naming them, not as a failure.

Worth knowing about the original warning, too, because it changes what this
check is for. `Cloning.WarnNotCloneable` is called from inside `Clone()`, not at
load, for everything except `ModItem` and `GlobalItem`, which warn from
`ValidateType`. So tModLoader does not warn about a non-cloneable `ModNPC` until
something actually clones one, which is why the ten-mod corpus log showed a
single warning while this check finds more. The check is not merely surfacing an
existing warning: for most kinds it is earlier than the warning.

*(Corrected later, by reading the callers rather than the warning.* **Nothing
clones an NPC or a projectile.** *`Item.Clone()` is the only place in the 1.4.5
tree that propagates to a mod's own instances, calling `ModItem.Clone` and
`GlobalItem.Clone` (`Item.cs.patch`); per-entity instances everywhere else come
from `NewInstance`, which builds a fresh object with `Activator.CreateInstance`
unless the type sets `CloneNewInstances`. So for every kind but the two item
ones, this check reports a conditional: what would be shared if something cloned
the subject. That is still tModLoader's own contract, and Daybreak enforces it at
load with an attribute, but it is not a live defect. See
`findings-in-other-mods.md`, which is where the corrected wording lives.)*

### The two that survive

Both name a field the mod declares, and one of them is a real defect in the
reference mod:

- `ExampleJavelinProjectile.stickingJavelins (Point[])` — one array per clone of
  a javelin, which today is never made. *(Said "every javelin shares one array"
  until the clone callers were read.)*
- `ExampleTownPet.NameList0` through `NameList6` (`List<string>`) — name tables
  that almost certainly want `[CloneByReference]`.

Neither is dramatic and both are exactly what the check is for. Whether they
are defects or annotations ExampleMod never needed is ExampleMod's call, which
is what the exemption mechanism is for.

## Known gaps

Recorded plainly rather than left to be discovered.

- **`CloneSweep` does not say which findings are conditional.** Only `ModItem`
  and `GlobalItem` are ever cloned by the game, so for every other kind the
  finding is about a clone nobody currently makes. The condition is still
  tModLoader's own, and worth reporting, but the message should distinguish the
  two rather than leaving the reader to work out which they have.

- **`IdSetSweep.RedirectionIsFlat` has no caller and no test.** *(Resolved: it is
  now exercised by a test and reachable through `SweepDeclarations`. See
  "Declaring a subject the sweep cannot find", which also corrects the claim that
  it needed a line in one particular repository.)*
- **`HjsonKeys` has no in-game caller, and cannot have one.** *(Resolved by
  moving it out: 6b is now an analyzer on a tModLoader branch, and nothing about
  it remains in this repository. See "Where 6b went".)*
- **2e, the lengthened-list mutation, is not written.** It is the only proposed
  check that would have found the corpus's one real persistence bug, so this is
  the most valuable gap on the list.
- **3b covers only the half of the banner condition that is reachable.** The
  rest compares against `NPCLoader.bannerToItem`, which is private.
- **The persistence and net ladders call live content.** Each rung saves first
  and restores afterwards, so a subject whose load is the inverse of its save is
  left as it was. One for which that is untrue cannot be perfectly restored, and
  that is the same thing the settling rung reports. Nothing a box can prevent:
  world data is world-global, which the calibration notes already record.

## What it produces, measured

Against ExampleMod with its own suite enabled, on tModLoader
`1.4.5.8+9999.0|2026.07|1.4.5|dev`. Sweep suites only; the run as a whole was
3187 tests.

| Suite | Cases | Failed | Skipped | Passed |
|---|---|---|---|---|
| ContentInvariantTests | 1041 | 0 | 135 | 906 |
| CloneTests | 267 | 2 | 14 | 251 |
| ReferenceTests | 192 | 0 | 10 | 182 |
| PropertySweepTests | 91 | 0 | 4 | 87 |
| ConfigTests | 36 | 0 | 1 | 35 |
| NetSymmetryTests | 30 | 0 | 0 | 30 |
| PersistenceTests | 24 | 0 | 0 | 24 |
| IdSetTests | 2 | 0 | 1 | 1 |
| LocalizationTests | 2 | 0 | 0 | 2 |

The two failures are the cloneability findings above. The single `ConfigTests`
skip is ExampleMod's declared exemption, reached through a declaration written
for a different ladder.

Three of these numbers are worth reading rather than skimming.

**`IdSetTests` found no subjects, so 3d is unexercised.** ExampleMod holds no
static array whose length equals a vanilla count, which is the correct outcome
for a mod that builds its sets through `XID.Sets.Factory`: the factory sizes to
the loaded count, so a correct set is never vanilla-length in a game with modded
content. The check ran and had nothing to look at. Its sibling,
`Every_space_counts_at_least_what_vanilla_did`, passed, which at least confirms
the two counts are being read the right way round, the exact mistake the corpus
survey found in TestingEfficiency's suite.

**It also cannot be self-tested, and that is a structural limit rather than an
oversight.** The obvious proof would be a deliberately vanilla-sized array in a
mod here, but every sweep excludes mods whose names end in `Test` or `Tests`,
so the framework's own mods are invisible to it. Exercising 3d needs either a
real mod that has the defect or a refactor that lifts the length-matching rule
into a pure function the core can test. The second is the better answer and is
not done.

**`NetSymmetryTests` is 30 cases over 10 subjects, all passing**, which matches
what the corpus survey predicted: the ladder finds nothing today. That is the
expected result for a check whose value is catching a regression, and it is
worth knowing it runs rather than assuming it would.

**`PersistenceTests` is 24 cases over 6 subjects with nothing skipped**, which
answers a question the implementation left open: a bare `new Player()` can be
constructed in a headless server and does carry a mod's own `ModPlayer`, so the
player half of the ladder runs against a throwaway carrier rather than skipping.

# Three questions, answered

## What a real 3d bug looks like, and why the sweep was withdrawn

The defect is concrete and worth writing out, because the shape is what the
withdrawn check was reaching for and missing:

```csharp
public static class MySets {
    public static bool[] IsSpecial = new bool[NPCID.Count];   // 697 slots
}
...
if (MySets.IsSpecial[npc.type])       // npc.type is 700-something for a modded NPC
```

`IndexOutOfRangeException`, on a lookup, which is the common path, so the
exception arrives nowhere near the line that caused it. `NPCID.Count` is
`static readonly short Count = 697` and tModLoader never changes it; the loaded
count is `NPCLoader.NPCCount`. A set built through `NPCID.Sets.Factory` is sized
correctly for free, because the patch to `NPCID` replaces
`new SetFactory(Count)` with `new SetFactory(NPCLoader.NPCCount, ...)`. The
hand-rolled array is the one that breaks.

**So the bug is real. The runtime check for it was not sound, and has been
removed.** Three independent reasons, and the third only appeared on contact
with the corpus:

1. **It detects a shape, not a defect.** `new bool[NPCID.Count]` is only wrong
   if a modded ID ever indexes it. A mod keeping a deliberately vanilla-only
   lookup is correct and indistinguishable from the air.
2. **Lengths coincide.** The check's whole signal was "length equals some
   vanilla count", so any array that happens to be 697 long looked like an NPC
   set.
3. **Finding the arrays is itself hazardous.** Reading every static field of
   every type in every mod's assembly forces type resolution and static
   initialisation across code nobody asked to run. Against the corpus it threw
   `FileNotFoundException: Could not load file or assembly 'Luminance'` for a
   weak-referenced assembly that was not installed, which turned the case source
   into a discovery error.

And it found nothing. No mod in the corpus has a short set. The one real defect
in this area was a *test* asserting against `NPCID.Count` where its own comment
said it meant the loaded count.

**Detecting it precisely means reading the size expression, not the length.**
`new bool[NPCID.Count]` versus `new bool[NPCLoader.NPCCount]` versus
`new bool[697]` are three different intentions that produce the same array, and
only the source distinguishes them. That is a syntactic question, so it belongs
to an analyzer, where it would have no coincidental false positives and could
name the fix.

One caveat before anybody writes it: `LoaderStateAnalyzer` deliberately stands
down in an assembly that declares an `ILoadable`, which is every mod assembly,
and that is exactly where this rule needs to run. So it cannot be a rule on the
existing analyzer; it needs a second analyzer with the opposite activation
condition. That also changes what the analyzer package is *for*, from "the tier 0
boundary" to "mod correctness", which is a scope decision rather than an
implementation detail, so it is proposed here rather than done.

What survives in `IdSetSweep` is the pair of assertions a mod's own suite calls
about sets whose meaning it knows, and `LoadedCount`, which exists so that the
bound is written once rather than guessed at each call site.

## What normally reads the hjson files, and where 6b belongs

Two different readers, and the distinction decides the answer.

**The packed copies, inside the `.tmod`,** are read by
`LocalizationLoader.Autoload` and `LoadModTranslations` at load. Those are the
files the game uses, and they are reachable only through `Mod.File`, which is
internal. That is why 6b cannot be a swept in-game check.

**The source copies, in the mod's folder,** are read *and written* by tModLoader
itself. `LocalizationLoader.UpdateLocalizationFilesForMod` regenerates them
after a build and reload, which is the mechanism by which new keys appear in the
hjson ready to be filled in. So the normal reader of the source files is the
loader's own file updater, in-game, during development. Its gates are worth
naming, because they are what keeps it from being a check: the mod's source
folder has to exist, a locally built `.tmod` has to sit in `ModLoader.ModPath`,
and the file on disk has to be older than that `.tmod`. A translation edited
after the last build is newer, so it is left alone.

**And they are already handed to analyzers.** `tMLMod.targets` carries

```xml
<AdditionalFiles Include="**/*.hjson" />
```

so every mod build already passes every localization file to whatever analyzers
are installed. That is where 6b belongs: it is a question about two files on
disk, it needs no game, and the plumbing to see the files exists and is already
switched on. It would report at edit time, in the editor, next to the line.

**Decision: 6b is not ours to run.** It belongs to tModLoader's own analyzers,
which already receive the files, and is worth raising with them as a feature
request rather than reimplemented here.

### Where 6b went

`HjsonKeys` and its tier 0 tests are gone from this repository. The check is now
`UnreachableLocalizationKeyAnalyzer` on a tModLoader branch,
`UnreachableLocalizationKeyAnalyzer`, based on upstream `1.4.5` at `39e7995fa5`
and not pushed anywhere. What the move changed, beyond the language:

- **The comparison unit is right.** An analyzer runs once per project, so the
  English side is the union of every `en-US` file in the mod, which is what the
  loader's one registry per mod actually does. `HjsonKeys.Unreachable` compared
  two files, and would have reported a key that merely moved between files.
- **Two false positives are gone.** A key holding a `$` is a variant, added by
  `LanguageManager.AddVariant` for whatever culture declares it and reachable
  with no English counterpart, and `$parentVal` is resolved to the key above it
  by `LoadTranslations`. Both were in the corpus's blind spot and neither was
  handled here.
- **A mod declaring `translationMod` is skipped**, because its English keys
  belong to the mod it translates and no compilation of it can see them.

Measured against ten published mods, Calamity and BossChecklist among them: 99
localization files, 12554 `en-US` keys and 2858 translated keys read, nothing
reported. Renaming one block of BossChecklist's `pl-PL` file reports exactly the
seven keys that file translates, the rest being commented out, which is how the
updater writes an untranslated entry.

**What is not established.** The analyzer's own unit tests cannot run without a
built tModLoader, which needs the full decompile-and-patch setup, so they are
written and syntax-checked rather than executed. Every case they assert was run
through the same analyzer-testing library they use, with the same expected spans
and arguments, by a standalone harness beside the branch; that harness is also
what measured the corpus above. Only the tModLoader metadata references differ
between the two.

## Opting in to a world per mutating test

`[MutatesGlobalState]` marks a test whose subject is state no box can contain and
which would rather have a world of its own. It is deliberately not
`[FreshWorld]`, and the difference is who decides:

- `[FreshWorld]` is a requirement. The test cannot mean anything without its own
  world, so a runner that cannot provide one reports a skip.
- `[MutatesGlobalState]` is a preference. The test restores what it changed and
  is honest in a shared world, but its subject is global, so isolating it is
  strictly better when the run can afford it.

Off by default. One switch opts the whole run in, because the cost is a world per
test and that is a decision about the run rather than about any one test:

```
ISOLATE_MUTATORS=1 FRESH_WORLD=1 scripts/run-tests.sh
```

`ISOLATE_MUTATORS=1` passes `-testariaisolatemutators`, and
`TestCase.NeedsOwnWorld` folds the two attributes into one question asked in a
single place. The catalogue asks it too, and that part is easy to forget: a
harness reads `tests.tsv` to decide which tests need a process of their own, so a
catalogue reporting only the attributes would have every mutating test share one
world while the switch claimed otherwise. `ToTsv` and `Summarize` take the switch
for that reason. Asking for the isolation without `FRESH_WORLD=1` is
reported per test as a skip naming the switch, rather than quietly ignored: the
run asked for something the host cannot give, and running anyway would answer a
question nobody asked.

Verified both ways. Ten tier-0 tests cover the discovery, the folding, and the
skip; and a real run with `ISOLATE_MUTATORS=1` and no `FRESH_WORLD=1` reported
**24 tests: 0 passed, 24 skipped** for the persistence ladder, which is the
behaviour that matters: asking for isolation the host cannot give stops the tests
rather than quietly running them in a shared world.

The persistence and net ladders carry the attribute. Both call a mod's real
hooks, both restore what they found, and both have world-global subjects, which
is the case the calibration notes already describe: a box isolates a region and
not a flag. A mod whose load is not the inverse of its save cannot be perfectly
restored, which is the very thing the settling rung reports, so the run most
likely to need the isolation is the one about to find something.

# Run against the corpus

Eleven mods plus the two suites that carry exemptions, on tModLoader
`1.4.5.8+9999.0|2026.07|1.4.5|dev`:

```
ENABLED="Testaria TestariaSweepTest ExampleMod ExampleModTests InnoVault
         Daybreak CheatSheet SilkyUIFramework TestingEfficiency BeardBench
         DragonLens RecipeBrowser"
```

**3761 tests: 3550 passed, 12 failed, 0 errored, 199 skipped.**

Five of the twelve were already known: two belong to ExampleMod's own suite, and
three are the SilkyUI properties that cannot be read on a server, which the
earlier calibration already recorded. **Seven are new, and one of those is
already declared intended by its author.**

## Two problems on our end, found only by doing this

Both were found by running, not by reading, and both would have gone unnoticed
against ExampleMod alone.

**The sweep hung the server.** The first corpus run never reported at all: the
log ends in repeated `Server hung for more than 10 seconds. Cannot determine
cause from watchdog thread`. The cause was the withdrawn 3d check reading every
static field of every type in every mod's assembly, which forces type resolution
and static initialisation across code nobody asked to run. It also threw
`FileNotFoundException` for `Luminance`, a weak-referenced assembly that is not
installed, turning a case source into a discovery error. Removing the check
removed the hang: the same run now completes with no watchdog warnings at all.
This is the fourth independent reason that check was wrong, and the only one that
could not have been reasoned out in advance.

**`GetMethod(name)` is ambiguous on real code.** Both ladders decided whether a
subject overrides a hook with `type.GetMethod(hook)`, which throws
`AmbiguousMatchException` as soon as a type has an overload of that name.
DragonLens has a `ModSystem` carrying its own generic `NetSend<T>(int, int)`
beside the `NetSend(BinaryWriter)` it inherits, and that turned three case
sources into discovery errors. Fixed by asking for the exact signature, in one
shared `ContentSweep.Overrides` so the two ladders cannot drift.

Neither was a finding about any mod. Both were the sweep being wrong about what a
subject is, which is the same category as the narrowings the earlier calibration
records.

## Findings on their end

### A player file that will not load, one release from now

**`DragonLens/MOTDPlayer`** fails two rungs of the persistence ladder:

```
ArgumentException: Version string portion was too short or too long. (Parameter 'input')
```

That is `new Version(...)` on a string that is empty or malformed. It fails both
the round trip and the absent-keys rung, which together say something worse than
either alone: the mod cannot read back even the tag its own `SaveData` just
wrote, for a player whose version field has not been set. `PlayerIO.LoadModData`
turns that throw into a `CustomModDataException`, and **the player file stops
opening.**

*(Both the inference and the severity were corrected afterwards by reading the
source, which is on GitHub even though the corpus carries only the `.tmod`.
`LoadData` is `Version.Parse(tag.GetString("seenMotd") ?? "0.0.0")`, and the
fallback is dead: `GetString` returns `""` rather than null for a missing key, so
`Version.Parse` is handed the empty string. The severity is lower than the
sentence above claims, and not by much: `PlayerIO.SaveModData` skips a `ModPlayer`
that wrote no keys, so no file carries the condition today, and the first release
whose `SaveData` writes any second key unconditionally gives it to every player
who has not seen the message. Written up properly in
`findings-in-other-mods.md`.)*

### Five types whose clones would share state

| Subject | Field the mod declares |
|---|---|
| `Daybreak/ItemDataProviderImpl` | `Dictionary<Type, IBoundDataProvider> DataProviders` |
| `TestingEfficiency/DamageStatsRecorder` | `int[] PlayerMiscDamage` |
| `TestingEfficiency/ProjectileSourceManager` | `IEntitySource source` |
| `ExampleMod/ExampleJavelinProjectile` | `Point[] stickingJavelins` |
| `ExampleMod/ExampleTownPet` | six `List<string>` name tables |

The Daybreak one is the single warning tModLoader itself emitted across this
corpus, and it is **intended**: Daybreak marks it `[ExpectCloneable(false)]` and
enforces that expectation at load. It is a true positive that wants an exemption,
which is exactly the case the check was designed around.

`TestingEfficiency/DamageStatsRecorder` is the most interesting of the rest. It is
a `GlobalNPC` in a mod whose whole purpose is measuring damage, holding a
per-player damage array that every clone shares. `ProjectileSourceManager`
sharing one `IEntitySource` is the same shape.

Whether each is a defect or an annotation nobody needed is the author's call.
That is what the exemption mechanism is for, and the message names the field so
the call can be made without reading the check.

## What found nothing

Worth recording, because the value of a check that finds nothing is that it
runs at all.

- **Net symmetry: 33 cases over 11 subjects, all passing.** Exactly what the
  survey predicted from reading the pairs by hand. The ladder is a regression
  guard here, not a discovery.
- **Config, localization, ID references, ID set sizing: nothing.** The
  localization check confirms no mod in the corpus ships a raw key, and the
  reference check confirms none points at content that does not exist.

# Declaring a subject the sweep cannot find

An earlier note here said `RedirectionIsFlat` needed "a line in TestingEfficiency's
suite". That was wrong in a way worth correcting, because it made a general
problem sound like a per-repository chore.

What a redirection check needs to know is three things: which ID space the
indices belong to, which value means "not redirected", and whether the values are
IDs *in that same space*. tModLoader's `SetFactory` already records the first two.
A named set knows its space, from the `SetFactory` it was created on, and its
default value, from the registration. So two of the three unknowns I had claimed
were unknowable are in fact written down.

The third is not, and cannot be inferred. `NPCID.Sets.NpcToBannerItem` is an
`int[]` indexed by NPC holding *item* IDs. `InvasionSlotCount` is an `int[]`
indexed by NPC holding *counts*. A redirection set is an `int[]` indexed by NPC
holding NPC IDs. Checking any of the three for chains is meaningful for exactly
one, and nothing distinguishes them.

So it is declared, and `SweepDeclarations` is the mechanism:

```csharp
SweepDeclarations.DeclareRedirection(
    "TestingEfficiency/NpcToCountAs", IDSets.NpcToCountAs, none: -1, space: "NPC");
```

One line, anywhere the knowledge lives. The mod's own suite is the most likely
place because that is where somebody can check the claim, but nothing requires it
to be there. A declaration is a claim about one specific array rather than a rule
about a kind of array, which is what stops the sweep inventing the standard it
then measures against.

`SweepDeclarations` is the mirror image of `SweepExemptions`: that one is for a
check the sweep would run and should not, this one for a check the sweep would run
and cannot, because it cannot recognise the subject. Declared sets arrive as cases
and are asked all four questions at once, sizing included.

The rule is also now exercised. `IdSetTests` makes it fire on a chain, a
self-reference, and an out of range target against sets made up on the spot,
because nothing in this repository has a real one and untried code in a test
framework is worse than absent code.

## A leak found on the way

`SweepExemptions.Clear()` has always documented itself as "called when the
framework unloads", and nothing called it. One run's declarations therefore
carried into the next, where the mod that made them may not be loaded. Both
registries are now cleared in `TestariaSystem.Unload`, and it matters more for
the new one: a declaration holds a reference to an array inside a mod's assembly,
so surviving a reload would keep that assembly alive, which is the exact leak
`mods/CLAUDE.md` warns about.

# The rest of the ladder, implemented

## 2e, a record longer than this build writes

The mutation is the cheapest honest stand-in for "a save from a build that stored
more": take the tag the mod's own `SaveData` produced and duplicate the entries of
every list in it, at every depth. It invents no values, so a loader is handed
exactly the kind of thing it already reads, only more of it.

This is the only proposed check that reaches the corpus's `CopyTo` defect.
CheatSheet's `LoadData` copies the saved accessory list into an array sized by a
mutable public static, and `List.CopyTo` throws when the source is longer than the
destination. An empty tag cannot reach it, because copying nothing always fits,
and a round trip cannot reach it, because both sides use today's size.

A skip when the record holds no lists, rather than a pass: a mod storing none
cannot fail this, and a pass would claim a check that never ran.

## 7a and 7b, placeability: implemented, then withdrawn

Both were written, run against the corpus, and removed. They reported **seven
failures against ExampleMod**, and the reference mod turned out to be right.

The reasoning was: `Main.tileFrameImportant` without a registered
`TileObjectData` makes an unplaceable tile, because `TileObject.CanPlace` fetches
the data, finds null, and returns false. Every step of that is true. What I never
checked is whether that path is reached.

It is not. `Player.PlaceThing_Tiles_TryPlacing` branches on
`TileObjectData.CustomPlace(type, style)`, **not** on `Main.tileFrameImportant`.
`CustomPlace` returns false when there is no data, and the `else` branch calls
`WorldGen.PlaceTile`. So a framed tile with no object data is placed by the plain
path and `CanPlace` is never consulted. ExampleMod's `ExampleTrap`,
`ExampleExposedGem` and `ExampleSlopeTile`, and the four items that place them,
are all perfectly placeable.

This is the same mistake as the withdrawn 3d check, made a different way: I
reasoned from a real function returning a real false without asking who calls it.
Reading one more frame up would have prevented both. The measurement is recorded
here because "seven failures against the reference mod" is the signal that
something is wrong with the check, and it is the second time that signal has been
right.

What survives is the half that was never about placement: an item whose
`createTile` names a tile that does not exist. That is an ID reference no loader
validates, and it is already checked by 3a in `ReferenceSweep`, so nothing is
lost by removing these two.

## The tier 2 family, and what it dropped

Behind `BEHAVIOUR=1`, because every case leases a box.

Each test **pauses the world and drives the entity by hand**, and that is not an
optimisation. An exception thrown inside the game's own NPC or projectile loop
surfaces on the game thread, where a test cannot catch it and tModLoader may
swallow it. The same `UpdateNPC` or `Update` called from the test is catchable and
can name the tick it failed on. `UpdateNPC` rather than `AI` alone, because the
bookkeeping around the AI is part of what a real tick does and part of what a
fragile AI leans on.

- **8a** runs each `ModNPC`'s own update for 120 ticks. An NPC that stops being
  active is not a failure: despawning is ordinary, and plenty do it at once on a
  server with nobody nearby.
- **8b** runs each `ModProjectile`'s the same way.
- **8c** places and mines each plain `ModTile`.

**Two claims from the proposal were dropped, and the reasons are the interesting
part.**

*"A projectile expires within its own `timeLeft`."* Withdrawn. A minion lives as
long as its buff and resets `timeLeft` every tick by design, and so do held
projectiles, so "makes progress toward expiry" is not an invariant. Detecting a
genuine pool leak needs to know which kind of projectile it is, which is exactly
the knowledge a sweep does not have.

*"Every `ModTile` places and mines."* Narrowed to plain blocks. A framed tile's
placement preconditions belong to the content: an anchor, a pocket of a given
size, a particular surface. ExampleMod's own suite already shows the cost of
ignoring that, reporting `ExampleDoorClosed` as placing nothing because the test
gave it no three-tile pocket. Asking generically would report the sweep's
ignorance as the mod's defect.

# The corpus, run with everything on

Twelve mods, behaviour tests enabled, after the placement checks were withdrawn.
**3846 tests: 3622 passed, 13 failed, 0 errored, 211 skipped.**

| Suite | Cases | Failed | Skipped | Passed |
|---|---|---|---|---|
| ContentInvariantTests | 1041 | 0 | 135 | 906 |
| PropertySweepTests | 571 | 3 | 38 | 530 |
| CloneTests | 279 | 5 | 14 | 260 |
| ReferenceTests | 192 | 0 | 10 | 182 |
| ConfigTests | 76 | 0 | 1 | 75 |
| PersistenceTests | 70 | 3 | 12 | 55 |
| BehaviourTests | 70 | 0 | 0 | 70 |
| NetSymmetryTests | 33 | 0 | 0 | 33 |
| LocalizationTests | 9 | 0 | 0 | 9 |
| IdSetTests | 3 | 0 | 1 | 2 |

Five of the thirteen were known before any of this: two in ExampleMod's own suite,
three SilkyUI properties unreadable on a server. **Eight are the sweep's**, and one
of those is declared intended by its author.

## 2e earns its place on the first run

**`CheatSheet/CheatSheetPlayer` fails `A_player_survives_a_longer_record`:**

```
ArgumentException: Destination array was not long enough.
```

This is the defect the corpus survey found by reading source, and predicted that no
other proposed check could reach. `LoadData` copies the saved accessory list into an
array sized by `MaxExtraAccessories`, a mutable public static rather than a
constant, using `List.CopyTo`, which throws when the source is longer than the
destination. A player file written while that number was higher does not load.

The prediction held in both directions, which is the part worth recording: the
empty-tag and round-trip rungs both **pass** for this same subject in this same
run. The claim that only a lengthened record could reach it was tested rather than
assumed.

## The rest

`DragonLens/MOTDPlayer` fails two rungs on a malformed version string, and five
types report clones sharing state. Both were described in the previous corpus
section and are unchanged by this round.

**Behaviour: 70 cases, nothing failed.** Every `ModNPC` and `ModProjectile` in the
corpus survived 120 ticks of its own update, and every solid modded block placed
and mined cleanly. Nothing found, which for a check that runs a mod's code rather
than reading it is worth knowing.

**Net symmetry, config, localization, ID references, ID sets: nothing.** Four of
those were predicted to find nothing by the survey, from reading the code by hand.

## Two mistakes of my own, for the record

Neither was a finding about any mod, and both are the kind that only running
produces.

**A check that hung the server.** The withdrawn 3d sweep read every static field of
every type in every mod's assembly, forcing type resolution and static
initialisation across code nobody asked to run. The first corpus run never
reported: repeated `Server hung for more than 10 seconds`, and a
`FileNotFoundException` for a weak-referenced assembly that was not installed.

**A script edited while it was running.** `run-tests.sh` was modified mid-run,
so bash resumed reading at a stale byte offset and misparsed a heredoc, failing the
post-run summary at a line inside embedded Python. The run's results were written
and intact; only the reporting step broke. Worth writing down because the symptom
pointed at a line that had nothing wrong with it.
