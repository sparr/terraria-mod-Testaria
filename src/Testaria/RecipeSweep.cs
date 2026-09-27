using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// Recipes a mod added, and the one thing that can be said about a recipe
/// without knowing what it is for.
/// <para/>
/// <b>Off unless asked for, and the reason is what it claims rather than what it
/// costs.</b> Reading a recipe is free. What makes this different from the rest
/// of the sweep is the kind of statement it makes: everything else says "this
/// will break", and a free-money loop says "this is unbalanced", which is the
/// author's business and may well be deliberate. Half the mods in the surveyed
/// corpus are cheat and tooling mods where crafting something out of itself is
/// the entire point. Mixing the two claims would make the report harder to
/// trust, so this one is asked for separately: <c>ECONOMY=1</c>, and see
/// <c>TestSession.EconomyFlag</c>.
/// <para/>
/// tModLoader validates recipes thoroughly at registration (<c>Recipe.TML.cs</c>
/// throws on a nonexistent ingredient, group, or station, on a recipe with no
/// result, and on the same recipe twice), so almost nothing else about a recipe
/// is worth checking here: a violation would never finish loading.
/// </summary>
public static class RecipeSweep
{
	/// <summary>
	/// Separates a recipe's result from which of that result's recipes it is.
	/// A mod may give one item several recipes, and each is its own case.
	/// </summary>
	public const char Ordinal = '#';

	/// <summary>
	/// Every recipe a loaded mod added, as <c>Mod/Result#n</c>, in the order the
	/// game holds them.
	/// <para/>
	/// The mod is the one that added the recipe rather than the one that owns the
	/// result, because the recipe is the subject and a mod adding a recipe for a
	/// vanilla item is answerable for it.
	/// </summary>
	public static IEnumerable<string> EveryModRecipe()
		=> Named()
			.Where(entry => entry.recipe.Mod is not null
				&& ContentSweep.IsSubjectMod(entry.recipe.Mod.Name))
			.Select(entry => entry.name);

	/// <summary>
	/// The name a vanilla recipe is reported under. Not a mod, and a loop through
	/// one still has to be printable.
	/// </summary>
	public const string Vanilla = "Terraria";

	/// <summary>
	/// Every loaded recipe with the name it is reported under, vanilla's included,
	/// in the order the game holds them.
	/// <para/>
	/// One numbering for everything, so that a name means the same thing to the
	/// case list and to the loop search. Numbering only the subjects would give
	/// the same recipe two names depending on who was asking.
	/// </summary>
	private static IEnumerable<(Recipe recipe, string name)> Named()
	{
		Dictionary<string, int> seen = [];

		for (int i = 0; i < Recipe.numRecipes; i++) {
			Recipe? recipe = Main.recipe[i];

			if (recipe is null)
				continue;

			string result = $"{recipe.Mod?.Name ?? Vanilla}{ContentSweep.Separator}{ItemName(recipe.createItem.type)}";
			seen.TryGetValue(result, out int n);
			seen[result] = n + 1;

			yield return (recipe, $"{result}{Ordinal}{n}");
		}
	}

	/// <summary>
	/// Asserts that this recipe is not part of a loop of recipes that hands back
	/// more than it was given.
	/// <para/>
	/// The loop is the subject rather than any one recipe in it, so every recipe
	/// in a loop reports the same loop. That is deliberate: each of them is
	/// defensible alone and any of them can be the one that breaks the cycle, so
	/// naming one of them the culprit would be the check inventing a judgement.
	/// <para/>
	/// <b>The graph includes vanilla's recipes</b>, because a mod can close a loop
	/// through them with a single recipe of its own, and that loop is the mod's to
	/// answer for. Only recipes a mod added are cases, so a loop made entirely of
	/// vanilla recipes is found and reported against nobody; there is none in
	/// 1.4.5.8, and if there were it would not be a mod author's problem.
	/// </summary>
	public static void IsNotPartOfAFreeOutputLoop(string qualified)
	{
		RecipeLoop[] loops = [.. Analysis().Loops.Where(loop => loop.Recipes.Contains(qualified))];

		Assert.True(loops.Length == 0, loops.Length == 0 ? null : Describe(qualified, loops));
	}

	/// <summary>
	/// Asserts that the loop search finished rather than running out of its own
	/// budget.
	/// <para/>
	/// One case for the whole run, because the answer is about the search and not
	/// about any recipe. Without it a bounded search that gave up looks exactly
	/// like a clean one, which is the failure mode a report cannot afford.
	/// </summary>
	public static void LoopSearchFinished()
	{
		RecipeLoopSearch search = Analysis();

		Assert.True(search.Complete,
			$"the search for free-output loops ran out of budget after "
			+ $"{RecipeLoops.DefaultBudget} steps, so it found {search.Loops.Count} "
			+ "and cannot say there are no others");
	}

	private static string Describe(string qualified, IReadOnlyList<RecipeLoop> loops)
	{
		var said = new System.Text.StringBuilder();

		said.Append(qualified).Append(" is part of ").Append(loops.Count == 1
			? "a loop that hands back more than it takes:"
			: $"{loops.Count} loops that hand back more than they take:");

		foreach (RecipeLoop loop in loops) {
			said.AppendLine().Append("  ")
				.Append(string.Join(" then ", loop.Recipes))
				.Append(", which returns ").Append(loop.GainNumerator)
				.Append(" for every ").Append(loop.GainDenominator).Append(" consumed")
				.AppendLine()
				.Append("    more of its own ingredients: ")
				.Append(string.Join(", ", loop.Items.Select(ItemName)));

			said.AppendLine().Append("    ").Append(loop.NewItems.Count == 0
				? "and nothing else becomes free through it"
				: "and these become free through it: "
					+ string.Join(", ", loop.NewItems.Select(ItemName)));
		}

		return said.ToString();
	}

	/// <summary>
	/// The loop search, run once and kept until the recipe list changes.
	/// <para/>
	/// Recipes are fixed after load, so this is computed on the first case that
	/// asks and reused by the rest: the alternative is the same graph search once
	/// per recipe. Keyed on the recipe count so a reload cannot be answered with
	/// the previous world's graph, and <see cref="Forget"/> drops it outright.
	/// </summary>
	public static RecipeLoopSearch Analysis()
	{
		if (analysis is not null && analysedAt == Recipe.numRecipes)
			return analysis;

		analysedAt = Recipe.numRecipes;

		return analysis = RecipeLoops.Find([.. Named().Select(entry => Step(entry.recipe, entry.name))]);
	}

	/// <summary>Drops the cached search, for a framework unload.</summary>
	public static void Forget()
	{
		analysis = null;
		analysedAt = -1;
	}

	/// <summary>
	/// One recipe as the search needs it: its result, and a slot per ingredient
	/// carrying every type that can satisfy it.
	/// </summary>
	private static RecipeStep Step(Recipe recipe, string name)
		=> new(
			name,
			recipe.createItem.type,
			recipe.createItem.stack,
			[.. recipe.requiredItem.Select(item => new RecipeSlot(item.stack, Accepts(recipe, item.type)))]);

	/// <summary>
	/// Every item type that satisfies an ingredient of this type: itself, plus the
	/// contents of any accepted group that holds it.
	/// <para/>
	/// That is exactly <c>Recipe.AcceptedByItemGroups</c>, which asks whether some
	/// accepted group holds both what the player has and what the recipe wants.
	/// Vanilla's hardcoded substitutions are <b>not</b> followed: <c>useWood</c>,
	/// <c>useSand</c>, <c>useIronBar</c>, <c>usePressurePlate</c> and
	/// <c>useFragment</c> are private predicates on <c>Recipe</c> rather than
	/// groups, so a loop that exists only through one of them is missed here.
	/// </summary>
	private static IReadOnlySet<int> Accepts(Recipe recipe, int type)
	{
		HashSet<int> accepts = [type];

		foreach (int group in recipe.acceptedGroups) {
			if (RecipeGroup.recipeGroups.TryGetValue(group, out RecipeGroup? valid)
				&& valid.ValidItems.Contains(type)) {
				accepts.UnionWith(valid.ValidItems);
			}
		}

		return accepts;
	}

	private static RecipeLoopSearch? analysis;
	private static int analysedAt = -1;

	/// <summary>
	/// Asserts that a recipe does not produce its own ingredient in at least the
	/// quantity it consumes.
	/// <para/>
	/// One ingredient, of the result's own type, and a result stack no smaller
	/// than the ingredient stack: craft, craft again, and the world has more of
	/// the item than it started with for no cost but time. Two ingredients cannot
	/// do it, because the second is consumed; a smaller result cannot, because
	/// that is a compression recipe and the inverse of this.
	/// <para/>
	/// Recipe groups are followed, since a group is how the loop usually arrives
	/// by accident: a single ingredient of some other type, accepted through a
	/// group that also contains the result, crafts the result from itself just as
	/// directly. <c>RecipeGroup.ValidItems</c> is the membership, and a group's
	/// contents are final by the time recipes exist.
	/// <para/>
	/// A disabled recipe is skipped rather than passed. It cannot be crafted, so
	/// the question does not arise, and a pass would claim an answer.
	/// </summary>
	public static void DoesNotDuplicateItsIngredient(string qualified)
	{
		Recipe recipe = Require(qualified);

		if (recipe.Disabled)
			Assert.Skip($"{qualified} is disabled, so it cannot be crafted at all.");

		Item result = recipe.createItem;
		Ingredient[] ingredients = [.. recipe.requiredItem.Select(item => new Ingredient(item.type, item.stack))];

		bool duplicates = RecipeShape.Duplicates(
			ingredients, result.type, result.stack, AcceptedTypes(recipe));

		// The message is built only when there is something to say. Composing it
		// unconditionally would read the ingredient that a recipe with two of
		// them does not have.
		Assert.False(duplicates, duplicates ? Describe(qualified, ingredients[0], result) : null);
	}

	private static string Describe(string qualified, Ingredient ingredient, Item result)
		=> $"{qualified} turns {ingredient.Stack} {ItemName(ingredient.Type)} into "
			+ $"{result.stack} {ItemName(result.type)}"
			+ (ingredient.Type == result.type ? "" : ", through a recipe group holding both,")
			+ " so crafting it repeatedly costs nothing and yields more of the item every time";

	/// <summary>
	/// Every item type the recipe's groups accept, which is what lets an
	/// ingredient of another type stand in for the result.
	/// <para/>
	/// Null rather than an empty set for a recipe with no groups, so the rule is
	/// handed nothing to look through.
	/// </summary>
	private static IReadOnlySet<int>? AcceptedTypes(Recipe recipe)
	{
		if (recipe.acceptedGroups.Count == 0)
			return null;

		HashSet<int> accepted = [];

		foreach (int group in recipe.acceptedGroups) {
			if (RecipeGroup.recipeGroups.TryGetValue(group, out RecipeGroup? valid))
				accepted.UnionWith(valid.ValidItems);
		}

		return accepted;
	}

	/// <summary>
	/// The recipe a case name refers to, or a skip if the recipe list is no
	/// longer what discovery saw.
	/// <para/>
	/// Recipes are fixed once <c>AddRecipes</c> has run, so this is a guard
	/// against a mod reloading underneath a run rather than something expected.
	/// </summary>
	private static Recipe Require(string qualified)
	{
		int split = qualified.LastIndexOf(Ordinal);
		// Assigned before it is tested, because definite assignment does not
		// follow [DoesNotReturn] and the failure below leaves the compiler
		// believing the ordinal may be unset.
		int wanted = -1;

		if (split <= 0 || !int.TryParse(qualified[(split + 1)..], out wanted))
			Assert.Fail($"'{qualified}' does not name a recipe: expected Mod{ContentSweep.Separator}Result{Ordinal}n.");

		string result = qualified[..split];
		int seen = 0;

		for (int i = 0; i < Recipe.numRecipes; i++) {
			Recipe? recipe = Main.recipe[i];

			if (recipe?.Mod is null || !ContentSweep.IsSubjectMod(recipe.Mod.Name))
				continue;

			if ($"{recipe.Mod.Name}{ContentSweep.Separator}{ItemName(recipe.createItem.type)}" != result)
				continue;

			if (seen++ == wanted)
				return recipe;
		}

		Assert.Skip($"{qualified} is no longer among the loaded recipes.");
		throw new InvalidOperationException("unreachable");
	}

	/// <summary>
	/// An item's internal name, which is what a report should print: a display
	/// name is localized and a bare number means nothing to the reader.
	/// </summary>
	private static string ItemName(int type)
	{
		if (ItemLoader.GetItem(type) is { } modded)
			return modded.Name;

		try {
			return ItemID.Search.GetName(type);
		}
		catch (Exception) {
			// A type with no name in either direction is worth printing as
			// itself rather than turning a report into an error.
			return type.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}
	}
}
