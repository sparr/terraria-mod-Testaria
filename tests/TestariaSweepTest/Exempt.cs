using Testaria;

namespace TestariaSweepTest;

/// <summary>
/// The one line every rung of every ladder needs: stand down when the mod that
/// knows has said this answer was intended.
/// <para/>
/// Shared rather than repeated in each suite. There are now four ladders here,
/// three rungs each, and a copy of this in every one of them would be twelve
/// places for the skip's wording to drift apart.
/// </summary>
internal static class Exempt
{
	/// <summary>
	/// Skips with the declared reason when a check does not apply to a subject.
	/// <para/>
	/// A skip carrying its reason, never a pass. A check that is not being
	/// applied has to say so and say why, which is the same argument
	/// <c>UNCOVERED</c> makes for a gate that will not run.
	/// </summary>
	internal static void Unless(string subject, SweepCheck check)
	{
		if (SweepExemptions.IsExempt(subject, check, out string reason))
			Assert.Skip($"{subject}'s own suite says this does not apply: {reason}");
	}
}
