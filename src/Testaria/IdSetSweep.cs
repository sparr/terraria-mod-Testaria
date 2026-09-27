using Terraria.ID;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// ID-indexed sets, and what has to be true of one whatever it means.
/// <para/>
/// An "ID set" is an array indexed by a content ID: <c>NPCID.Sets.IsTownPet</c>
/// and everything shaped like it, including the ones a mod declares for its own
/// use. Two mods in the surveyed corpus test their own by hand, which is what
/// says these are worth having: the failures are real, they are silent, and none
/// of them needs to know what the set means.
/// <para/>
/// <b>Nothing here is swept, and that is a conclusion rather than an omission.</b>
/// A first attempt swept every static array in every mod whose length equalled
/// some ID space's vanilla count. It was withdrawn for three independent
/// reasons, recorded because the idea is tempting enough to be had again:
/// <list type="number">
/// <item>It detects a shape, not a defect. <c>new bool[NPCID.Count]</c> only
/// breaks if a modded ID ever indexes it, and a mod keeping a vanilla-only
/// lookup is correct. The check could not tell the two apart.</item>
/// <item>Lengths coincide. Any array of exactly 697 entries looked like an NPC
/// set whether or not it was one.</item>
/// <item>Finding the arrays is itself hazardous. Reading every static field in
/// every mod's assembly forces type resolution and static initialisation across
/// code that was never asked to run: against the corpus it threw
/// <c>FileNotFoundException</c> for a weak-referenced assembly that was not
/// installed, turning a case source into a discovery error.</item>
/// </list>
/// It also found nothing. Across the whole corpus there was no mod with a
/// short set, and the one real defect in this area was a <i>test</i> asserting
/// against <c>NPCID.Count</c> where it meant <c>NPCLoader.NPCCount</c>.
/// <para/>
/// Detecting the real thing precisely needs the array's <i>size expression</i>,
/// not its length, which is a syntactic question and belongs to an analyzer
/// rather than to a sweep. What is left here is the pair of assertions a mod's
/// own suite can call about sets it knows the meaning of, and the bound it
/// should use, which is the part people get wrong.
/// </summary>
public static class IdSetSweep
{
	/// <summary>
	/// An ID space, as the pair of numbers that matters: what vanilla counted,
	/// and what is loaded now.
	/// </summary>
	public readonly record struct Space(string Name, int Vanilla, int Loaded)
	{
		/// <summary>Whether any mod has added to this space, so the two counts differ.</summary>
		public bool Grew => Loaded > Vanilla;
	}

	/// <summary>
	/// The ID spaces this knows how to count.
	/// <para/>
	/// Read fresh on every call rather than cached. These are loaded counts:
	/// they change across a reload, and a static cache would carry one run's
	/// numbers into the next.
	/// <para/>
	/// Each vanilla count is the <c>XID.Count</c> field, which is worth a word of
	/// warning because it reads like it would grow and does not.
	/// <c>NPCID.Count</c> is <c>static readonly short Count = 697</c> and
	/// tModLoader never changes it; the loaded count lives on the loader. Using
	/// the former where the latter is meant is the mistake this file exists to
	/// prevent, and it has already been made in a suite written against this
	/// framework.
	/// </summary>
	public static IEnumerable<Space> Spaces => [
		new("Item", ItemID.Count, ItemLoader.ItemCount),
		new("NPC", NPCID.Count, NPCLoader.NPCCount),
		new("Projectile", ProjectileID.Count, ProjectileLoader.ProjectileCount),
		new("Tile", TileID.Count, TileLoader.TileCount),
		new("Wall", WallID.Count, WallLoader.WallCount),
		new("Buff", BuffID.Count, BuffLoader.BuffCount),
	];

	/// <summary>
	/// The loaded count for a space, by the name <see cref="Spaces"/> uses.
	/// <para/>
	/// For a suite that wants the right bound without repeating the mapping,
	/// which is the mapping people get wrong.
	/// </summary>
	public static int LoadedCount(string space)
	{
		foreach (Space candidate in Spaces) {
			if (candidate.Name == space)
				return candidate.Loaded;
		}

		Assert.Fail($"'{space}' is not an ID space this knows how to count. "
			+ $"Known: {string.Join(", ", Spaces.Select(s => s.Name))}.");
		return 0;
	}

	/// <summary>
	/// Every redirection set anybody has declared, by name, as cases.
	/// <para/>
	/// Declared rather than discovered, and <see cref="SweepDeclarations"/>
	/// explains why at length: the one fact needed to check a redirection set is
	/// whether its values are IDs in the same space as its indices, and that is
	/// the one fact nothing records. A declaration is one line, anywhere the
	/// knowledge lives.
	/// </summary>
	public static IEnumerable<string> DeclaredRedirections()
		=> SweepDeclarations.Redirections.Select(set => set.Name).Order();

	/// <summary>
	/// Asserts that a declared set is a single hop and covers its space.
	/// <para/>
	/// Both questions at once, because a declaration is a promise about one
	/// array and there is no reason to make somebody declare it twice.
	/// </summary>
	public static void ADeclaredRedirectionIsSound(string name)
	{
		if (SweepDeclarations.Redirection(name) is not RedirectionSet declared) {
			Assert.Skip($"nothing called {name} is declared any more, so there is "
				+ "nothing to check.");
			return;
		}

		IsSizedForLoadedContent(declared.Set, declared.Space, declared.Name);
		RedirectionIsFlat(declared.Set, declared.None, declared.Space, declared.Name);
	}

	/// <summary>
	/// Asserts that a set covers every ID in its space.
	/// <para/>
	/// The bound is the loader's count, never <c>XID.Count</c>. A set longer than
	/// the count is fine and common, since the factory hands out one array per
	/// space and content can be registered after it: the question is only whether
	/// every loaded ID can be looked up.
	/// </summary>
	/// <param name="set">The set, as any array.</param>
	/// <param name="space">Which space it is indexed by, one of <see cref="Spaces"/>.</param>
	/// <param name="name">What to call it when this fails.</param>
	public static void IsSizedForLoadedContent(Array set, string space, string name)
	{
		Assert.NotNull(set, $"{name} is null, so nothing can be looked up in it");

		int loaded = LoadedCount(space);

		Assert.True(set.Length >= loaded,
			$"{name} is sized {set.Length}, which does not cover every loaded {space} ID: "
			+ $"there are {loaded}, so looking up the last {loaded - set.Length} throws "
			+ "IndexOutOfRangeException");
	}

	/// <summary>
	/// Asserts that a set mapping IDs to IDs is a single hop: every target
	/// exists, nothing points at itself, and nothing points at something that is
	/// itself redirected.
	/// <para/>
	/// The chain is the interesting one and the reason this is a check rather
	/// than a convention. Code that consults a redirection set almost always
	/// reads it once rather than following it to a fixed point, so a second hop
	/// does not throw and does not look wrong: it quietly credits the middle of
	/// the chain. Nothing else in a suite would notice, which is exactly the
	/// shape of thing worth asserting while it still holds.
	/// <para/>
	/// Not swept, and the reason is the parameter list. Which space the values
	/// belong to, and which value means "no redirection", are facts about the mod
	/// that declared the set. A sweep guessing at them would be inventing the
	/// standard it then measured against.
	/// </summary>
	/// <param name="set">The set, indexed by ID, holding IDs.</param>
	/// <param name="none">The value meaning "not redirected", usually -1 or 0.</param>
	/// <param name="space">Which space both the indices and the values belong to.</param>
	/// <param name="name">What to call it when this fails.</param>
	public static void RedirectionIsFlat(int[] set, int none, string space, string name)
		=> RedirectionIsFlat(set, none, LoadedCount(space), space, name);

	/// <summary>
	/// The same, against a bound given outright.
	/// <para/>
	/// For a set whose space this does not know how to count, and for exercising
	/// the rule without a loaded game.
	/// </summary>
	public static void RedirectionIsFlat(int[] set, int none, int loadedCount, string space, string name)
	{
		Assert.NotNull(set, $"{name} is null, so nothing can be looked up in it");

		List<string> problems = [];

		for (int id = 0; id < set.Length; id++) {
			int target = set[id];

			if (target == none)
				continue;

			if (target < 0 || target >= loadedCount) {
				problems.Add($"{id} points at {target}, which is not a loaded {space} ID "
					+ $"(there are {loadedCount})");
				continue;
			}

			if (target == id) {
				problems.Add($"{id} points at itself, which says nothing and costs a lookup");
				continue;
			}

			// Only meaningful where the target is in range, which is why this
			// comes after the bounds test rather than beside it: indexing the set
			// to ask about the target is the very thing being guarded.
			if (target < set.Length && set[target] != none) {
				problems.Add($"{id} points at {target}, which points at {set[target]}: "
					+ "a chain, and a reader that takes one hop lands in the middle of it");
			}
		}

		if (problems.Count == 0)
			return;

		Assert.Fail($"{name} is not a single hop, in {problems.Count} "
			+ (problems.Count == 1 ? "place" : "places") + ":\n  "
			+ string.Join("\n  ", problems));
	}
}
