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
}
