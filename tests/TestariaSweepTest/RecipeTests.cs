using Testaria;

namespace TestariaSweepTest;

/// <summary>
/// That a mod's recipes do not hand out free items.
/// <para/>
/// <b>Off unless asked for.</b> Not because it is expensive, which it is not, but
/// because of what it claims: every other check in this suite says a mod will
/// break, and this one says a mod is unbalanced. Those are different statements
/// and a report that mixes them is worth less than one that does not. Run with
/// <c>ECONOMY=1</c>.
/// <para/>
/// <see cref="RecipeSweep"/> carries the reasoning about what counts as a loop,
/// including why recipe groups are followed and why a smaller result is not one.
/// </summary>
public class RecipeTests
{
	public static IEnumerable<string> Recipes => RecipeSweep.EveryModRecipe();

	/// <summary>
	/// Stands down unless the run asked. A skip carrying its reason rather than
	/// an absent test, so a report cannot read as though every mod's recipes had
	/// been checked.
	/// </summary>
	private static void OnlyWhenAsked()
	{
		if (!TestSession.EconomyRequested) {
			Assert.Skip("the economy checks were not asked for. A free-money recipe is a "
				+ "balance claim rather than a defect, and in a cheat mod it is deliberate, "
				+ "so they are off by default: run with ECONOMY=1.");
		}
	}

	[LoadedTest]
	[CaseSource(nameof(Recipes))]
	public void A_recipe_does_not_craft_its_own_ingredient(string qualified)
	{
		OnlyWhenAsked();

		RecipeSweep.DoesNotDuplicateItsIngredient(qualified);
	}

	/// <summary>
	/// The same defect spread over several recipes, which is the form it survives
	/// review in: wood into sticks into wood, where the round trip comes back with
	/// more wood than it started with. The message distinguishes what the loop
	/// hands back more of, which is one of its own ingredients, from what becomes
	/// free through it, which is a different item entirely.
	/// </summary>
	[LoadedTest]
	[CaseSource(nameof(Recipes))]
	public void A_recipe_is_not_part_of_a_free_output_loop(string qualified)
	{
		OnlyWhenAsked();

		RecipeSweep.IsNotPartOfAFreeOutputLoop(qualified);
	}

	/// <summary>
	/// One case for the run rather than one per recipe, because it is about the
	/// search and not about any recipe: a bounded search that gave up looks
	/// exactly like a clean one unless somebody asks.
	/// </summary>
	[LoadedTest]
	public void The_free_output_loop_search_finished()
	{
		OnlyWhenAsked();

		RecipeSweep.LoopSearchFinished();
	}
}
