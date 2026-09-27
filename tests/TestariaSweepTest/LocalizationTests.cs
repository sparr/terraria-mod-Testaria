using Testaria;

namespace TestariaSweepTest;

/// <summary>
/// That every string a mod ships is text rather than a key.
/// <para/>
/// <see cref="ContentInvariantTests"/> already asks this of item, NPC and buff
/// display names, one case each. This asks it of everything else at once:
/// tooltips, map entries, town NPC dialogue, config labels and descriptions,
/// death messages, and any key a mod invented for itself. The overlap with
/// those three is deliberate. They name the individual piece of content, which
/// is what somebody fixing a shipped item wants; this names the mod and lists
/// whatever is unfilled, which is what somebody auditing a mod wants.
/// </summary>
public class LocalizationTests
{
	public static IEnumerable<string> Localized => LocalizationSweep.Localized();

	/// <summary>
	/// No key renders as its own key.
	/// <para/>
	/// One case per mod rather than per key, because a mod has thousands of
	/// keys and at most a handful of unfilled ones. The message names every one
	/// of them.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Localized))]
	public void Every_key_is_filled_in(string modName)
		=> LocalizationSweep.EveryKeyIsFilled(modName);

	/// <summary>
	/// The sweep found text to look at.
	/// <para/>
	/// Reported rather than assumed, for the reason
	/// <see cref="PropertySweepTests.The_sweep_has_subjects"/> gives: a run with
	/// no mod enabled but the harness turns the case above into nothing at all,
	/// and a suite of no cases reads like one that passed.
	/// </summary>
	[LoadedTest]
	public void The_sweep_has_subjects()
		=> Assert.NotEmpty(Localized,
			"no mod besides the harness registers any localized text");
}
