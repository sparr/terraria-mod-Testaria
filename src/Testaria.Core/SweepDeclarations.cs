namespace Testaria;

/// <summary>
/// A set that maps IDs to IDs in the same space, as declared by somebody who
/// knows that is what it is.
/// </summary>
/// <param name="Name">What to call it in a report. Conventionally <c>Mod/SetName</c>.</param>
/// <param name="Set">The array, indexed by ID, holding IDs.</param>
/// <param name="None">The value meaning "not redirected", usually -1 or 0.</param>
/// <param name="Space">Which ID space both the indices and the values belong to.</param>
public readonly record struct RedirectionSet(string Name, int[] Set, int None, string Space);

/// <summary>
/// Subjects the sweep cannot find for itself, declared by whoever knows they
/// exist.
/// <para/>
/// The companion to <see cref="SweepExemptions"/>, and its mirror image. That
/// one is for a check the sweep would run and should not; this is for a check
/// the sweep would run and cannot, because it cannot recognise the subject.
/// <para/>
/// Redirection sets are the case that prompted it, and they are worth spelling
/// out because it is a near miss. tModLoader's <c>SetFactory</c> does record
/// most of what a sweep would need: a named set knows its ID space, from the
/// <c>SetFactory</c> it was created on, and its default value, from the
/// registration. What it does not record is whether an <c>int[]</c> holds
/// <i>IDs in that same space</i>. <c>NPCID.Sets.NpcToBannerItem</c> holds item
/// IDs, <c>InvasionSlotCount</c> holds counts, and a redirection set holds NPC
/// IDs, and all three are an <c>int[]</c> indexed by NPC. Checking any of them
/// for chains would be meaningless for two out of three, so the one fact a
/// sweep needs is the one nothing records.
/// <para/>
/// So it is declared, in one line, by whoever knows. That is deliberately not
/// "a line in the mod's own repository": it is a line anywhere the knowledge
/// lives, and the mod's own suite is merely the most likely place because that
/// is where somebody can check the claim. A declaration made here is a claim
/// about a specific array, not a rule about a kind of array, which is what
/// keeps the sweep from inventing the standard it measures against.
/// </summary>
public static class SweepDeclarations
{
	private static readonly Dictionary<string, RedirectionSet> redirections = new();

	/// <summary>
	/// Declares that an array maps IDs to IDs in one space, so the sweep can ask
	/// whether it is a single hop.
	/// <para/>
	/// The array is held by reference rather than copied, on purpose: a set can
	/// be added to after it is declared, and the check should see what the game
	/// will see rather than what was true at load.
	/// </summary>
	/// <param name="name">What to call it in a report. Conventionally <c>Mod/SetName</c>.</param>
	/// <param name="set">The array, indexed by ID, holding IDs.</param>
	/// <param name="none">The value meaning "not redirected", usually -1 or 0.</param>
	/// <param name="space">Which ID space the indices and the values share.</param>
	public static void DeclareRedirection(string name, int[] set, int none, string space)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException("A declared set needs a name.", nameof(name));

		ArgumentNullException.ThrowIfNull(set);

		if (string.IsNullOrWhiteSpace(space))
			throw new ArgumentException("A declared set needs an ID space.", nameof(space));

		redirections[name] = new RedirectionSet(name, set, none, space);
	}

	/// <summary>Every redirection set anybody has declared.</summary>
	public static IEnumerable<RedirectionSet> Redirections => redirections.Values;

	/// <summary>One declared set, or null when nothing of that name was declared.</summary>
	public static RedirectionSet? Redirection(string name)
		=> redirections.TryGetValue(name, out RedirectionSet found) ? found : null;

	/// <summary>
	/// Forgets everything declared.
	/// <para/>
	/// Called when the framework unloads, and it matters more here than for
	/// exemptions: a declaration holds a reference to an array that lives in a
	/// mod's assembly, so one surviving a reload would keep that assembly alive
	/// and hand the next run the previous build's data.
	/// </summary>
	public static void Clear() => redirections.Clear();
}
