using System.Text.RegularExpressions;
using Terraria.Localization;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// The localized text a mod ships, and the one thing that has to be true of
/// all of it: that it is text rather than a key.
/// <para/>
/// An unfilled key renders as <c>Mods.SomeMod.Items.Foo.DisplayName</c>. It is
/// not empty, it is not null, and it looks like a string to every check that
/// only asks whether a name exists, so it reaches a player as gibberish. The
/// content invariants already ask this of item, NPC and buff display names;
/// this asks it of everything, because the same failure reaches a player
/// through tooltips, map entries, town NPC dialogue, config labels and death
/// messages, none of which was covered.
/// <para/>
/// The signature is exact rather than heuristic.
/// <c>LanguageManager.GetOrRegister</c> registers an unknown key with its own
/// key as its value, so <c>value == key</c> is precisely what "nobody filled
/// this in" looks like, and nothing else produces it.
/// </summary>
public static class LocalizationSweep
{
	/// <summary>
	/// Every loaded mod that has any localized text at all, as its name.
	/// <para/>
	/// One case per mod rather than one per key, which is a departure from how
	/// the rest of the sweep is arranged and is worth the explanation. A mod
	/// has thousands of keys and either none or a handful of unfilled ones, so
	/// per-key cases would add several thousand results that could never fail
	/// to make a handful of findings visible. The failure message names every
	/// offending key, so nothing is lost except the noise.
	/// <para/>
	/// Mods with no localized text are left out rather than passing vacuously.
	/// </summary>
	public static IEnumerable<string> Localized()
		=> ModLoader.Mods
			.Where(mod => !IsHarness(mod.Name))
			.Where(mod => Keys(mod.Name).Any())
			.Select(mod => mod.Name)
			.Order();

	/// <summary>
	/// Every localization key a mod owns, with whatever it currently renders as.
	/// <para/>
	/// Found by pattern over the registered text rather than by reading the
	/// mod's files, because the files are inside the <c>.tmod</c> and the
	/// loader does not open them to anybody else. What is registered is also
	/// the more honest subject: it is what a player will actually see.
	/// </summary>
	public static IEnumerable<LocalizedText> Keys(string modName)
		=> Language.FindAll(new Regex($"^Mods\\.{Regex.Escape(modName)}\\."));

	/// <summary>
	/// Asserts that none of a mod's keys renders as itself.
	/// <para/>
	/// Empty is allowed, and deliberately: a mod with an item that has no
	/// tooltip ships <c>Tooltip: ""</c> on purpose, and the surveyed corpus
	/// contains five such entries in one mod's English file. A check for
	/// non-empty text would have reported all five as defects on first
	/// contact, which is how a report teaches its reader to skim.
	/// <para/>
	/// Locale-independent, and that is a fact about tModLoader rather than a
	/// concession here. Mod keys are registered from the <c>en-US</c> file
	/// alone (<c>LocalizationLoader.Autoload</c>) and other cultures may only
	/// update the value of a key that already exists, while English is always
	/// loaded as the base layer beneath the active culture
	/// (<c>LanguageManager.ReloadLanguage</c>). So a mod shipping only English
	/// renders English everywhere, and a key renders as itself only when it is
	/// missing from English too. This check therefore neither needs pinning to
	/// a locale nor says anything about how completely a mod is translated,
	/// which is not a defect.
	/// </summary>
	public static void EveryKeyIsFilled(string modName)
	{
		List<string> raw = [];
		int total = 0;

		foreach (LocalizedText text in Keys(modName)) {
			total++;

			if (text.Value == text.Key)
				raw.Add(text.Key);
		}

		if (total == 0)
			Assert.Skip($"{modName} registers no localized text, so there is nothing to fill in.");

		if (raw.Count == 0)
			return;

		const int Listed = 20;

		string shown = string.Join("\n  ", raw.Take(Listed));

		if (raw.Count > Listed)
			shown += $"\n  ... and {raw.Count - Listed} more";

		Assert.Fail($"{modName} has {raw.Count} of {total} localization "
			+ (raw.Count == 1 ? "keys that renders" : "keys that render")
			+ " as the key itself, which is what a player sees:\n  " + shown);
	}

	/// <inheritdoc cref="TypeSweep.EveryConstructibleType"/>
	private static bool IsHarness(string modName)
		=> modName == "ModLoader"
			|| modName == nameof(Testaria)
			|| modName.EndsWith("Test", StringComparison.Ordinal)
			|| modName.EndsWith("Tests", StringComparison.Ordinal);
}
