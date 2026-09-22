using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Testaria;

namespace TestariaExampleTest;

/// <summary>
/// Crafting, which is where a content mod's mistakes tend to hide.
/// <para/>
/// A broken recipe does not crash anything. It simply never appears, or
/// appears and cannot be made, and neither shows up anywhere except a player's
/// confusion.
/// </summary>
public class RecipeTests
{
	/// <summary>Recipes whose result belongs to the subject.</summary>
	private static List<Recipe> SubjectRecipes()
	{
		HashSet<int> mine = [.. Subject.Content<ModItem>().Select(i => i.Type)];
		List<Recipe> recipes = [];

		for (int i = 0; i < Recipe.numRecipes; i++) {
			Recipe recipe = Main.recipe[i];

			if (recipe.createItem is { type: > ItemID.None } created && mine.Contains(created.type))
				recipes.Add(recipe);
		}

		return recipes;
	}

	[LoadedTest]
	public void The_subject_contributes_recipes()
		=> Assert.NotEmpty(SubjectRecipes());

	[LoadedTest]
	public void Every_recipe_makes_at_least_one_item()
	{
		foreach (Recipe recipe in SubjectRecipes())
			Assert.True(recipe.createItem.stack >= 1, $"a recipe for '{recipe.createItem.Name}' makes {recipe.createItem.stack} of it");
	}

	[LoadedTest]
	public void Every_recipe_needs_at_least_one_ingredient()
	{
		// A recipe with no ingredients is craftable from nothing, which is
		// almost never intended and is easy to reach by editing a list wrong.
		foreach (Recipe recipe in SubjectRecipes())
			Assert.NotEmpty(recipe.requiredItem);
	}

	[LoadedTest]
	public void No_recipe_asks_for_a_nonexistent_ingredient()
	{
		foreach (Recipe recipe in SubjectRecipes()) {
			foreach (Item ingredient in recipe.requiredItem) {
				Assert.True(ingredient.type > ItemID.None, $"a recipe for '{recipe.createItem.Name}' requires item type {ingredient.type}");
				Assert.True(ingredient.stack >= 1, $"a recipe for '{recipe.createItem.Name}' requires {ingredient.stack} of '{ingredient.Name}'");
			}
		}
	}

	[LoadedTest]
	public void Recipes_that_consume_their_own_output_are_named()
	{
		// Legal, and sometimes deliberate: a recipe converting between
		// variants of a thing will list it. Worth surfacing rather than
		// forbidding, so the report names each one instead of asserting.
		List<string> selfReferencing = [];

		foreach (Recipe recipe in SubjectRecipes()) {
			foreach (Item ingredient in recipe.requiredItem) {
				if (ingredient.type == recipe.createItem.type)
					selfReferencing.Add($"{recipe.createItem.Name} (id {recipe.createItem.type})");
			}
		}

		// The subject is a demonstration mod, so it shows this off on purpose.
		// The assertion is that the framework can see it, not that it is wrong.
		Assert.NotEmpty(selfReferencing);
	}
}
