using Testaria;

namespace TestariaSweepTest;

/// <summary>
/// That a mod's content can be copied without its copies sharing state.
/// <para/>
/// The condition is tModLoader's own and so is the answer: it computes
/// <c>IsCloneable</c> for every registered type and writes a warning to the log
/// when it is false. This turns that warning into a result, which is the whole
/// of the contribution. A log line at load is read once, by the author, on the
/// day they wrote the type.
/// <para/>
/// Exemptions are expected here rather than unusual, which is why they get a
/// mention in the check's own documentation. A framework deliberately sharing
/// one registry between every clone is correct and unprovable from here.
/// Daybreak, in the surveyed corpus, reached the same conclusion and built the
/// same mechanism for itself: an <c>[ExpectCloneable(false)]</c> attribute and a
/// load-time enforcer that throws when a type's <c>IsCloneable</c> disagrees
/// with what it declares. <see cref="SweepExemptions"/> is that idea with a
/// different spelling, and the mod that knows is the one that declares.
/// <para/>
/// Which is why a mod that has already declared it needs no entry here.
/// <see cref="DeclaredCloneability"/> reads an <c>ExpectCloneable</c>-shaped
/// attribute wherever one exists, and <c>CloneSweep</c> skips on it, so the
/// declaration stays in the repository that can check it against the code.
/// An exemption is for the mod that has said nothing anywhere else.
/// </summary>
public class CloneTests
{
	public static IEnumerable<string> Cloneable => CloneSweep.Every();

	[LoadedTest]
	[CaseSource(nameof(Cloneable))]
	public void Content_is_cloneable(string qualified)
	{
		if (SweepExemptions.IsExempt(qualified, SweepCheck.Cloning, out string reason))
			Assert.Skip($"{qualified}'s own suite says this does not apply: {reason}");

		CloneSweep.IsCloneable(qualified);
	}
}
