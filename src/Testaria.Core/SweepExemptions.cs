namespace Testaria;

/// <summary>Which of the sweeping checks an exemption is about.</summary>
public enum SweepCheck
{
	/// <summary>That every property can be read at all.</summary>
	GetterReads,

	/// <summary>That a setter accepts what its own getter produced.</summary>
	RoundTrip,

	/// <summary>That writing a value back twice changes nothing the second time.</summary>
	Settling,

	/// <summary>
	/// That a piece of content can be safely cloned, which tModLoader computes
	/// for every registered type and only warns about.
	/// <para/>
	/// The one check here whose exemptions are expected rather than unusual. A
	/// framework mod deliberately sharing a registry between clones is doing
	/// something correct that <c>IsCloneable</c> cannot distinguish from a
	/// mistake, and the surveyed corpus contains exactly such a case, annotated
	/// by its author as intended.
	/// </summary>
	Cloning,
}

/// <summary>
/// Sweeping checks a mod's own suite has declared do not apply to it.
/// <para/>
/// The sweeping checks are asked of every type in every loaded mod, and know
/// nothing about any of them. Occasionally one of them is answered by design:
/// ExampleMod has a configuration property that adds 0.2 to whatever it is
/// given, on purpose, to show that a setter may. Reporting that forever
/// teaches a reader to skim the report, and the next real finding goes with
/// it.
/// <para/>
/// The exemption belongs to whoever knows it is intended, which is the mod's
/// own test suite, not the sweep. A list inside the sweep would make this
/// framework the keeper of facts about mods it has never read, and would grow
/// one line per mod that anyone ever points it at. A suite declares its own:
/// <code>
/// SweepExemptions.Declare(
///     "ExampleMod/ExampleMod.Common.Configs.ModConfigShowcases.ModConfigShowcaseAccessibility",
///     SweepCheck.Settling,
///     "Property adds 0.2 to whatever it is given, on purpose.");
/// </code>
/// <para/>
/// An exemption is reported as a skip carrying its reason, never as a pass. A
/// check that is not being applied has to say so and say why, which is the
/// same argument <c>UNCOVERED</c> makes for a gate that will not run.
/// <para/>
/// A mod that already states the same thing in its own vocabulary needs no
/// entry here. <see cref="DeclaredCloneability"/> reads such a statement where
/// the cloning check is concerned, and a declaration that lives beside the code
/// beats one restated here: it cannot drift from the type it describes, and its
/// author maintains it without knowing this framework exists.
/// </summary>
public static class SweepExemptions
{
	private static readonly Dictionary<(string Subject, SweepCheck Check), string> declared = new();

	/// <summary>
	/// Declares that a check does not apply to a type, and why.
	/// <para/>
	/// The subject is the qualified name a sweep produces, <c>Mod/Type.Full.Name</c>.
	/// The reason is not optional and is shown wherever the skip is: an
	/// exemption nobody can evaluate later is indistinguishable from one that
	/// was wrong when it was made.
	/// </summary>
	public static void Declare(string subject, SweepCheck check, string reason)
	{
		if (string.IsNullOrWhiteSpace(subject))
			throw new ArgumentException("An exemption needs a subject.", nameof(subject));

		if (string.IsNullOrWhiteSpace(reason))
			throw new ArgumentException("An exemption needs a reason.", nameof(reason));

		declared[(subject, check)] = reason;
	}

	/// <summary>Whether a check has been declared not to apply, and why not.</summary>
	public static bool IsExempt(string subject, SweepCheck check, out string reason)
		=> declared.TryGetValue((subject, check), out reason!);

	/// <summary>
	/// Everything declared, for a suite that wants to report on its own
	/// exemptions rather than only act on them.
	/// </summary>
	public static IEnumerable<(string Subject, SweepCheck Check, string Reason)> All
		=> declared.Select(entry => (entry.Key.Subject, entry.Key.Check, entry.Value));

	/// <summary>
	/// Forgets everything declared.
	/// <para/>
	/// Called when the framework unloads. Static state that outlives a mod
	/// reload keeps the old assembly alive and carries one run's declarations
	/// into the next, where the mod that made them may not even be loaded.
	/// </summary>
	public static void Clear() => declared.Clear();
}
