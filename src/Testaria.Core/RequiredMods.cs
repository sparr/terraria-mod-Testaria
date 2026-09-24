namespace Testaria;

/// <summary>
/// The mods a run was promised, and which of them never turned up.
/// <para/>
/// A suite whose subject failed to load is the quietest way for a run to prove
/// nothing. tModLoader disables a mod that throws during its load pass and
/// carries on, so the suite either finds no tests or skips all of them, and a
/// clean report comes out the other end looking exactly like a passing one.
/// Measured: a mod that threw from <c>Load()</c> took its test mod down with
/// it, and the run reported "0 tests: 0 passed" and exited 0.
/// <para/>
/// <c>--require</c> catches this by counting, but only if somebody remembered
/// to set a number, and it can only say "fewer tests than I expected" rather
/// than which mod is missing. This says the latter, and needs no number: the
/// harness already knows what it installed, so it can simply say so and let
/// the run check.
/// <para/>
/// Lives in the core, with no reference to the game, so the parsing and
/// comparison can be tested without one.
/// </summary>
public static class RequiredMods
{
	/// <summary>
	/// Launch parameter naming the mods that must be loaded for a run to mean
	/// anything. Comma separated, since a mod name cannot contain a comma.
	/// </summary>
	public const string Flag = "-testariarequiremods";

	/// <summary>
	/// Splits the flag's value into mod names.
	/// <para/>
	/// Tolerant about separators and spacing because this is assembled by
	/// shell scripts as often as by the tool, and an accidental double comma
	/// should not become a mod named "".
	/// </summary>
	public static IReadOnlyList<string> Parse(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return [];

		return [.. value
			.Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Distinct(StringComparer.Ordinal)];
	}

	/// <summary>
	/// Which of the required mods are not among the loaded ones, in the order
	/// they were required.
	/// <para/>
	/// Ordinal comparison: tModLoader mod names are case sensitive
	/// identifiers, and treating "examplemod" as "ExampleMod" here would let a
	/// run pass a check that the game itself would not.
	/// </summary>
	public static IReadOnlyList<string> Missing(IEnumerable<string> required, IEnumerable<string> loaded)
	{
		ArgumentNullException.ThrowIfNull(required);
		ArgumentNullException.ThrowIfNull(loaded);

		HashSet<string> present = new(loaded, StringComparer.Ordinal);

		return [.. required.Where(name => !present.Contains(name))];
	}

	/// <summary>
	/// What to tell somebody whose run is about to be refused.
	/// <para/>
	/// Names the missing mods first, because that is the answer, and lists
	/// what did load second, because the usual cause is a load error that
	/// scrolled past and the list is how you notice the mod is simply absent
	/// rather than misspelled.
	/// </summary>
	public static string Describe(IReadOnlyList<string> missing, IEnumerable<string> loaded)
	{
		ArgumentNullException.ThrowIfNull(missing);
		ArgumentNullException.ThrowIfNull(loaded);

		if (missing.Count == 0)
			throw new ArgumentException("Nothing is missing, so there is nothing to describe.", nameof(missing));

		string subject = missing.Count == 1 ? "mod" : "mods";

		return $"This run required {subject} {string.Join(", ", missing)}, which did not load. "
			+ "A mod that throws during its load pass is disabled and the game carries on, so a suite "
			+ "aimed at it finds nothing to test and reports a clean run. Check the log for the load error. "
			+ $"Loaded: {string.Join(", ", loaded)}.";
	}
}
