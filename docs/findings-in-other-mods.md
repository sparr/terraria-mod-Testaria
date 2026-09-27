# Findings in other people's mods

Eight failures the sweeping checks reported against mods this repository did
not write, from the twelve-mod run recorded at the end of
`proposed-invariants.md`, on tModLoader `1.4.5.8+9999.0|2026.07|1.4.5|dev`.

This is the working document behind the reports, and the reason it exists
separately is that a report to somebody else's project has to survive their
reading of it: every claim below was re-derived from source after the run, and
three of the eight turned out to be weaker than the run's own description of
them.

## Where each one stands

| # | Subject | State |
|---|---|---|
| 1 | `CheatSheet/CheatSheetPlayer` | Reported, open: [JavidPack/CheatSheet#100](https://github.com/JavidPack/CheatSheet/issues/100). |
| 2 | `DragonLens/MOTDPlayer` | Reported, open: [ScalarVector1/DragonLens#164](https://github.com/ScalarVector1/DragonLens/issues/164). |
| 3 | `TestingEfficiency/DamageStatsRecorder` | Reported with 4, open: [Doze-Zoze/TestingEfficiency#12](https://github.com/Doze-Zoze/TestingEfficiency/issues/12). Still failing the sweep. |
| 4 | `TestingEfficiency/ProjectileSourceManager` | Same issue as 3. Still failing the sweep. |
| 5 | `ExampleMod/ExampleJavelinProjectile` | Fixed, `ead01a5b5e` on tModLoader's `examplemod-tests`. |
| 6 | `ExampleMod/ExampleTownPet` | Fixed, `210d13831b` on the same branch. |
| 7 | `Daybreak/ItemDataProviderImpl` | Declared intended, `2dce2ef` in `DaybreakTests`. |

All three issues were filed on 2026-09-27, are open, and have no replies yet.
Findings 3 and 4 went as one issue, since they are one mistake made twice in
one mod. Nothing is pushed: the two ExampleMod fixes and the Daybreak
declaration are local commits on branches in those checkouts.

A cloning sweep over `Testaria TestariaSweepTest ExampleMod CheatSheet
DragonLens InnoVault Daybreak DaybreakTests TestingEfficiency` now reports 278
cases, 2 failed, 16 skipped. The two failures are findings 3 and 4, which are
the mod author's to act on.

## What changed under checking, before anything else

Two claims made in `proposed-invariants.md` are overstated, and the corrections
matter more than the findings they belong to.

**Only items are cloned.** `Item.Clone()` is the one place in the 1.4.5 tree
that propagates to a mod's own instances: it calls `ModItem.Clone(newItem)` and
`GlobalItem.Clone(this, newItem)` (`patches/tModLoader/Terraria/Item.cs.patch`).
Nothing clones an `NPC` or a `Projectile` into their mod instances. Per-entity
instances arrive instead through `NewInstance`, which builds a fresh object with
`Activator.CreateInstance` unless the type opts into `CloneNewInstances`
(`ModType.cs:129`, `GlobalType.cs:154`), so field initializers run per entity.
So "every javelin shares one array" is wrong: every javelin has its own. The
sharing is conditional on a clone that the game never performs for that kind.

**A cloneability finding is a contract violation, not a live bug**, for every
kind except `ModItem` and `GlobalItem`. tModLoader's own standard is
`Cloning.IsCloneable`: a type with reference fields and no `Clone` override, no
`[CloneByReference]` on those fields, is reported. That standard is worth
holding to, because it is the loader's, and Daybreak enforces it at load with
an attribute of its own. It is not a promise that anything is currently broken.

The findings are ordered by what a user would notice, not by the check that
found them.

## 1. Cheat Sheet: a longer accessory list stops a player file loading

**Subject** `CheatSheet/CheatSheetPlayer`.
**Check** 2e, `A_player_survives_a_longer_record`: take the tag the mod's own
`SaveData` produced, duplicate the entries of every list in it, and load that.
**Result** `ArgumentException: Destination array was not long enough.`

Verified in source, `CheatSheet/CheatSheetPlayer.cs`:

```csharp
public static int MaxExtraAccessories = 6;                        // line 11
public Item[] ExtraAccessories = new Item[MaxExtraAccessories];   // line 12

public override void Initialize() {
    ExtraAccessories = new Item[MaxExtraAccessories];             // line 34
    ...
}

public override void LoadData(TagCompound tag) {
    tag.GetList<TagCompound>("ExtraAccessories").Select(ItemIO.Load).ToList()
        .CopyTo(ExtraAccessories);                                // line 47
}
```

`List<T>.CopyTo(T[])` throws when the source is longer than the destination, and
the destination is sized by a mutable `public static` rather than a constant.
`SaveData` writes the whole array as a list, so a file written while that number
was larger holds more entries than the array a later session allocates.
`PlayerIO.LoadModData` turns the throw into a `CustomModDataException`
(`PlayerIO.cs:267`), which is a player file that will not open.

**How reachable it is, stated honestly.** Nothing in Cheat Sheet assigns
`MaxExtraAccessories`; it is 6 in every build we can see. Cheat Sheet itself can
lower it in any release, which breaks every file saved beforehand. Another mod
can raise it, but not by referencing it: `CheatSheetPlayer` is `internal`
(`CheatSheetPlayer.cs:9`), there is no `Mod.Call` message for it
(`CheatSheet.cs:384-403`), and the only `InternalsVisibleTo` is for
`CheatSheetTests` (`TestVisibility.cs:11`). The route is reflection, which is
ordinary enough in this ecosystem where no cross-mod API exists. The sequence
that kills a file is install-then-remove: an add-on raises the number, the
player saves ten entries, the add-on is disabled, and the next load allocates
six. The run produced the condition synthetically rather than finding a file in
the wild, and the report should say so.

**Suggested fix.** Copy what fits rather than asserting that it does:

```csharp
var saved = tag.GetList<TagCompound>("ExtraAccessories").Select(ItemIO.Load).ToList();
for (int i = 0; i < Math.Min(saved.Count, ExtraAccessories.Length); i++)
    ExtraAccessories[i] = saved[i];
```

Sizing the array from the saved list instead would keep the extra accessories,
at the cost of an array longer than the rest of the mod expects.

## 2. DragonLens: a load that cannot read its own absent field

**Subject** `DragonLens/MOTDPlayer`.
**Checks** 2b and 2c, the round trip and the absent-keys rung, both failing.
**Result** `ArgumentException: Version string portion was too short or too long.
(Parameter 'input')`

Source read from `ScalarVector1/DragonLens`, `Core/Systems/MOTDSystem.cs`, since
the corpus carries DragonLens as a `.tmod` with no source in it:

```csharp
public Version seenMotd;

public override void SaveData(TagCompound tag)
{
    if (seenMotd != null)
        tag["seenMotd"] = seenMotd.ToString();
}

public override void LoadData(TagCompound tag)
{
    seenMotd = Version.Parse(tag.GetString("seenMotd") ?? "0.0.0");
}
```

The `?? "0.0.0"` never fires. `TagCompound.GetString` is `Get<string>`, which for
a missing key deserializes the payload handler's default, and the handler for
`string` returns `""` (`TagIO.cs:99-107`, `TagCompound.cs:30-42`). So the
argument is the empty string, and `Version.Parse("")` throws.

`seenMotd` is null for anybody whose `OnEnterWorld` returned early, which is
every player without tool permissions, so `SaveData` writing nothing is the
ordinary case rather than an edge.

**How reachable it is.** Not yet. `PlayerIO.SaveModData` skips a `ModPlayer`
whose `SaveData` wrote no keys (`PlayerIO.cs:233`), so today no player file
carries a DragonLens entry without `seenMotd`, and `LoadData` is never called
with one. The failure is one unconditional write away: the first release whose
`SaveData` also stores something else, for anybody who has not seen the message,
produces an entry that `LoadData` throws on, and
`PlayerIO.LoadModData` turns that into a `CustomModDataException` and an
unopenable player file.

It has to be an unconditional key, and a second conditional one changes
nothing. `tag["seenMotd"] = seenMotd?.ToString()` is not a trigger either, since
`TagCompound.Set` removes the key when the value is null
(`TagCompound.cs:74-79`), leaving the entry empty as before. Only
`MOTDPlayer`'s own keys matter: entries are per `ModPlayer`.

**Suggested fix.** `Version.TryParse`, or read the key defensively:

```csharp
seenMotd = tag.TryGet("seenMotd", out string saved) && Version.TryParse(saved, out var parsed)
    ? parsed
    : new Version(0, 0, 0);
```

## 3 and 4. Testing Efficiency: two per-entity globals share state when cloned

**Subjects** `TestingEfficiency/DamageStatsRecorder`, a `GlobalNPC`, and
`TestingEfficiency/ProjectileSourceManager`, a `GlobalProjectile`. Both declare
`InstancePerEntity => true`.
**Check** 4a, `A_type_is_cloneable_as_it_declares`, against
`Cloning.IsCloneable`.
**Fields the mod declares** `int[] PlayerMiscDamage`
(`DamageStats/DamageStatsRecorders.cs:23`) and `IEntitySource source`
(`DamageStats/ProjectileSourceManager.cs:11`). The sibling `sourceData` is a
struct (`DataStructures.cs:65`) and is not part of the finding.

Neither is broken today, for the reason given at the top: nothing in the 1.4.5
tree clones an NPC or a projectile into its globals, so each entity gets its own
instance and its own array. What the check reports is that if either type is
ever cloned, by a future tModLoader or by another mod calling `Clone`, the copies
share the array and the source, and for a mod whose purpose is measuring damage
per NPC that would be a silent wrong number rather than a crash.

Why `sourceData` is exempt and `source` is not, since a report will be asked:
`DeepCloning.NeedsFieldClone` recurses into a struct and asks the same question
of its fields (`Testing/Cloning/StructTypeInfo.cs:12`). `DamageSourceData` holds
two `int`s and two enums, so a memberwise copy gives each instance its own, and
the struct is skipped. A reference field is copied as a pointer, so both
instances name one object. "It is a struct" alone would not be the reason: a
struct holding a `List<T>` is reported like any other field.

**Suggested fix**, whichever is true of the field: a `Clone` override that
copies the array, or `[CloneByReference]` where sharing is intended. The second
is a one-line statement that the author looked. For `source` the annotation is
probably right, since an `IEntitySource` is a record of where something came
from; for `PlayerMiscDamage`, accumulated per NPC, it is not.

**Reported upstream.** Both still fail the sweep, which is expected until the
mod acts, and they are the only failures left in the corpus run.

## 5 and 6. ExampleMod: two subjects that wanted the annotation, and got it

**Subjects** `ExampleMod/ExampleJavelinProjectile` and
`ExampleMod/ExampleTownPet`.
**Check** the same 4a.
**Fields** `readonly Point[] stickingJavelins`
(`Content/Projectiles/ExampleJavelinProjectile.cs:176`), a scratch buffer passed
to `KillOldestJavelin`; and six `readonly List<string>` name tables,
`NameList0`, `NameList1`, `NameList3` through `NameList6`
(`Content/NPCs/TownPets/ExampleTownPet.cs:124-141`).

The name tables are constant data that nothing mutates, which is exactly what
`[CloneByReference]` is for. The javelin buffer is per-instance scratch, and
since `ModProjectile` is never cloned by the game it is never shared; if it
were, two javelins would share one sticking list.

**Fixed rather than reported**, since this repository holds a branch on
tModLoader. `210d13831b` annotates the six name tables; `ead01a5b5e` gives the
javelin a `Clone` override that copies the buffer, which meant dropping
`readonly` from the field. The two resolve differently on purpose, and each
file's comment says why and points at the other: the annotation is for constant
data that is only read, the override for scratch space that is written per
entity. `ModType.NewInstance` calls `Clone` whenever `CloneNewInstances` is
true, so a mod opting into that flag gets the sharing immediately, and a
reference mod demonstrating the opt-in is the right place to demonstrate the
contract that goes with it.

Afterwards the javelin passes outright and `ExampleTownPet` reports the skip
fifteen other ExampleMod NPCs already report: not cloneable, with every
remaining field belonging to tModLoader rather than to the mod.

## 7. Daybreak: declared intended, and the one live clone path

**Subject** `Daybreak/ItemDataProviderImpl`, a `GlobalItem`.
**Field** `Dictionary<Type, IBoundDataProvider> DataProviders`.

This is the only finding on a kind the game actually clones, since
`Item.Clone()` calls `GlobalItem.Clone`. It is also deliberate: Daybreak marks
the type `[ExpectCloneable(false)]` and enforces that expectation at load
(`src/Daybreak/Common/CodeAnalysis/CloneabilityContracts.cs:30-58`,
`src/Daybreak/Common/Features/Models/ItemDataProvider.cs:31`). Our check and
their attribute agree, which is the most useful thing about this entry.

**No report to send, and now a declaration.** `2dce2ef` on the checkout's
`testaria-tests` branch adds `src/DaybreakTests/SweepDeclarations.cs`, which
declares the exemption with the mod's own reason: the `DataProviders` map is
rebuilt by hand in `NewInstance`, cloning each provider into a fresh dictionary,
rather than carried over by a memberwise copy. It sits beside the attribute it
agrees with, where one reader can check both.

**What was tried instead, and withdrawn.** The sweep briefly read
`ExpectCloneable`-shaped attributes directly, so that no declaration would be
needed. It was withdrawn: GitHub code search finds the name in exactly one
repository, and one assembly of the 53 installed here, so the code was a special
case for Daybreak wearing a general shape. What replaced it is the convention
mods do share, an override of tModLoader's own `IsCloneable`, which
`DeclaredCloneability` reads and Daybreak does not use. A mod whose only
statement is in a library's vocabulary declares it through `SweepExemptions`,
which is where this started.

## What is left

- A reply on any of the three issues, none of which has one yet.
- Nothing is pushed. The ExampleMod fixes sit on `examplemod-tests` in the
  tModLoader checkout, the declaration on `testaria-tests` in the Daybreak
  checkout, and pushing either is a separate decision.
- Findings 3 and 4 stay open in the sweep until their author acts, which is the
  correct state for a reported finding rather than something to silence.

The standards these were written to, kept here because the next report will
want them: issue text reflowed rather than wrapped, per this repository's own
convention for text aimed at humans on GitHub; permalinks into the mod's own
source at a pinned commit rather than quoted excerpts; a repro a maintainer can
run without installing this framework, which for 1 and 2 is a player file plus
the two lines that break it and for 3 to 6 is the sentence from
`Cloning.IsCloneable` and the field name; a decision per mod about whether it
is worth their time, since 3 to 6 are contract hygiene and were offered as such
rather than as bug reports; and a disclosure paragraph saying that a sweep run
by an AI found the failure and that a human read the code before filing, which
all three carry.
