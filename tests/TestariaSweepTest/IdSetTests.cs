using Testaria;

namespace TestariaSweepTest;

/// <summary>
/// The one thing about ID sets that can be asked without knowing what any set
/// means: that this framework reads the two counts the right way round.
/// <para/>
/// <see cref="IdSetSweep"/> explains at length why the rest is not swept. The
/// short of it is that a set's length cannot distinguish a defect from a
/// coincidence, and finding the arrays at all means forcing type resolution
/// across code that was never asked to run. What remains for a mod's own suite
/// is <see cref="IdSetSweep.IsSizedForLoadedContent"/> and
/// <see cref="IdSetSweep.RedirectionIsFlat"/>.
/// </summary>
public class IdSetTests
{
	/// <summary>
	/// Every space reports a loaded count at least as large as vanilla's.
	/// <para/>
	/// A check on the framework rather than on any mod, and cheap insurance for
	/// the assertions it offers: both compare a length against a loader count,
	/// and reading <c>XID.Count</c> where the loaded count is meant makes them
	/// quietly wrong in the permissive direction. That is not hypothetical. The
	/// corpus survey found a mod's own suite asserting
	/// <c>Assert.Equal(NPCID.Count, length)</c> under a comment saying it meant
	/// the loaded count, which passes only in a run where no mod adds an NPC.
	/// </summary>
	[LoadedTest]
	public void Every_space_counts_at_least_what_vanilla_did()
	{
		Assert.NotEmpty(IdSetSweep.Spaces, "no ID spaces are known, so nothing can be sized");

		foreach (IdSetSweep.Space space in IdSetSweep.Spaces) {
			Assert.True(space.Loaded >= space.Vanilla,
				$"{space.Name} reports {space.Loaded} loaded against {space.Vanilla} in vanilla, "
				+ "so the two counts have been read the wrong way round");
		}
	}

	public static IEnumerable<string> Declared => IdSetSweep.DeclaredRedirections();

	/// <summary>
	/// Every redirection set a mod's suite has declared is a single hop and
	/// covers its space.
	/// <para/>
	/// No case until somebody declares one, which is the honest shape for a check
	/// whose subject cannot be discovered. A mod that has such a set adds one
	/// line and gets all four questions asked of it.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Declared))]
	public void A_declared_redirection_is_sound(string name)
		=> IdSetSweep.ADeclaredRedirectionIsSound(name);

	/// <summary>
	/// The redirection rule catches a chain, a self-reference, and an out of
	/// range target.
	/// <para/>
	/// Exercised here against sets made up on the spot, because nothing in this
	/// repository declares a real one and the corpus mod that does is in another
	/// repository. Untried code in a test framework is worse than absent code,
	/// so the rule is at least made to fire once.
	/// </summary>
	[LoadedTest]
	public void The_redirection_rule_catches_what_it_is_for()
	{
		// A clean set: two parts crediting one whole, nothing chained.
		IdSetSweep.RedirectionIsFlat([-1, -1, 0, 0], -1, 4, "NPC", "a flat set");

		// A chain: 3 points at 2, which points at 0.
		Assert.Throws<AssertionException>(() =>
			IdSetSweep.RedirectionIsFlat([-1, -1, 0, 2], -1, 4, "NPC", "a chained set"));

		// Something pointing at itself.
		Assert.Throws<AssertionException>(() =>
			IdSetSweep.RedirectionIsFlat([-1, -1, 2, -1], -1, 4, "NPC", "a self-pointing set"));

		// A target past the end of the space.
		Assert.Throws<AssertionException>(() =>
			IdSetSweep.RedirectionIsFlat([-1, -1, 99, -1], -1, 4, "NPC", "an out of range set"));

		// The sizing rule, likewise made to fire.
		IdSetSweep.IsSizedForLoadedContent(new bool[IdSetSweep.LoadedCount("NPC")], "NPC", "a full set");
	}
}
