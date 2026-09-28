using Testaria;

namespace TestariaSweepTest;

/// <summary>
/// Both property checks, asked of every constructible type in every mod that
/// happens to be installed.
/// <para/>
/// This suite references no mod and names none. The two assertions need a
/// subject and no understanding of it, and <see cref="TypeSweep"/> turns "every
/// type in every loaded mod" into cases, so the subject list is decided by
/// which mods a run enables rather than by anything written here. Point it at a
/// mod by enabling that mod:
/// <code>
/// ENABLED="Testaria TestariaSweepTest InnoVault CheatSheet" scripts/run-tests.sh
/// </code>
/// <para/>
/// It is the generic half of what a mod's own suite does in ten lines. A suite
/// living with its mod is still the better home for anything that knows what
/// the mod is; this is for the question that does not.
/// <para/>
/// Nothing here is part of the green path. It reports on whatever is installed,
/// so its result is a property of the machine rather than of this framework,
/// which is why it is not one of the gates.
/// </summary>
public class PropertySweepTests
{
	public static IEnumerable<string> Everything => TypeSweep.EveryConstructibleType();

	/// <summary>
	/// The sweep found something to look at.
	/// <para/>
	/// Reported rather than assumed, because a run with no mods enabled but
	/// this one turns every case below into nothing at all, and a suite of no
	/// cases reads like a suite that passed.
	/// </summary>
	[LoadedTest]
	public void The_sweep_has_subjects()
		=> Assert.NotEmpty(Everything,
			"no mod besides the harness is enabled, so this suite has nothing to sweep");

	/// <summary>
	/// Reading a property and writing it straight back must not throw, which is
	/// what a text field does every time somebody opens one and closes it
	/// again.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Everything))]
	public void Takes_its_own_values(string qualified)
		=> Run(qualified, SweepCheck.RoundTrip, PropertyScope.RoundTrippable,
			subject => Assert.SettersAcceptTheirOwnGetters(subject));

	/// <summary>
	/// And writing it back twice must change nothing the second time. The first
	/// write may normalize what it was given; a value that keeps moving has no
	/// resting state.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Everything))]
	public void Settles_after_one_write(string qualified)
		=> Run(qualified, SweepCheck.Settling, PropertyScope.RoundTrippable,
			subject => Assert.SettersSettleAfterOneWrite(subject));

	/// <summary>
	/// Every property can be read at all, which is the plainest of the three
	/// and the one most often worth knowing. A getter is expected to answer,
	/// and a great deal of code reads properties without being asked to.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Everything))]
	public void Can_be_read(string qualified)
		=> Run(qualified, SweepCheck.GetterReads, PropertyScope.Readable,
			subject => Assert.GettersDoNotThrow(subject));

	/// <summary>
	/// One case, unless the mod's own suite has declared that this check does
	/// not apply to it, in which case its reason is reported as a skip.
	/// <para/>
	/// The declaration lives with the mod that knows, not here. A list in this
	/// file would make the sweep the keeper of facts about mods it has never
	/// read, and would grow a line for every mod anybody ever points it at.
	/// <para/>
	/// Or unless another subject is asked the identical question, which
	/// happens in bulk when a mod generates a family of types over one
	/// inherited auto-property. <c>TypeSweep.IsAnsweredByAnother</c> holds the
	/// condition for that; the skip names the subject that carries the
	/// question, so a reader can go and see the answer rather than wonder
	/// where it went.
	/// </summary>
	private static void Run(string qualified, SweepCheck check, PropertyScope scope,
		Action<object> assertion)
	{
		if (SweepExemptions.IsExempt(qualified, check, out string reason))
			Assert.Skip($"{qualified}'s own suite says this does not apply: {reason}");

		if (TypeSweep.IsAnsweredByAnother(qualified, scope, out string representative)) {
			Assert.Skip("every property this would be asked about is an auto-property it "
				+ $"inherits, so the question is the same one {representative} is asked and "
				+ "is answered there.");
		}

		assertion(TypeSweep.ConstructQualified(qualified));
	}
}
