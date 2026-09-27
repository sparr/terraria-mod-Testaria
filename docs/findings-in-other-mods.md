# Findings in other people's mods

Eight failures the sweeping checks reported against mods this repository did
not write, from the twelve-mod run recorded at the end of
`proposed-invariants.md`, on tModLoader `1.4.5.8+9999.0|2026.07|1.4.5|dev`.

**Nothing here has been sent anywhere.** No issue, no pull request, no
message. This is the working document behind those, and the reason it exists
separately is that a report to somebody else's project has to survive their
reading of it: every claim below was re-derived from source after the run, and
three of the eight turned out to be weaker than the run's own description of
them.

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
`MaxExtraAccessories`; it is 6 in every build we can see. The field is public and
static, so another mod can raise it, and Cheat Sheet itself can lower it in any
release. Both produce the failure for a file saved beforehand. The run produced
the condition synthetically rather than finding a file in the wild, and the
report should say so.

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

**Suggested fix**, whichever is true of the field: a `Clone` override that
copies the array, or `[CloneByReference]` where sharing is intended. The second
is a one-line statement that the author looked.

## 5 and 6. ExampleMod: two subjects that want the annotation

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

Worth saying plainly in any report to the reference mod: this is the loader's own
cloneability contract, and the file that teaches people how to write mods is a
good place to show the annotation being used.

## 7. Daybreak: declared intended, and the one live clone path

**Subject** `Daybreak/ItemDataProviderImpl`, a `GlobalItem`.
**Field** `Dictionary<Type, IBoundDataProvider> DataProviders`.

This is the only finding on a kind the game actually clones, since
`Item.Clone()` calls `GlobalItem.Clone`. It is also deliberate: Daybreak marks
the type `[ExpectCloneable(false)]` and enforces that expectation at load
(`src/Daybreak/Common/CodeAnalysis/CloneabilityContracts.cs:30-58`,
`src/Daybreak/Common/Features/Models/ItemDataProvider.cs:31`). Our check and
their attribute agree, which is the most useful thing about this entry.

**No report to send.** What it wants is an exemption on our side, declared by
whoever knows, which is what `SweepExemptions` is for. Recorded here so that the
count of eight is accounted for rather than quietly reduced to seven.

## What each of these needs before it goes anywhere

- Issue text reflowed rather than wrapped, per this repository's own convention
  for text aimed at humans on GitHub.
- A repro a maintainer can run without installing this framework: for 1 and 2,
  a player file plus the two lines that break it; for 3 to 6, the sentence from
  `Cloning.IsCloneable` and the field name.
- A decision per mod about whether it is worth their time. Findings 3 to 6 are
  contract hygiene and should be offered as such, not as bug reports.
