namespace Testaria.Tests;

/// <summary>
/// <see cref="RecipeShape.Duplicates"/>, which is the whole of the free-money
/// rule. The sweep that calls it cannot be tested in this repository, because
/// every sweep excludes mods whose names end in Test, so this is where the rule
/// is shown to fire and, more importantly, shown not to.
/// </summary>
public class RecipeShapeTests
{
	private const int Wood = 9;
	private const int Stone = 1;

	[Fact]
	public void One_of_an_item_into_two_of_it_is_a_loop()
		=> XAssert.True(RecipeShape.Duplicates([new Ingredient(Wood, 1)], Wood, 2));

	[Fact]
	public void One_into_one_of_the_same_item_is_a_loop_too()
		// Nothing is gained, but nothing is spent either, and the recipe exists
		// to no end. Reported rather than excused, because an item that crafts
		// itself one for one is almost always a mistake in the ingredient.
		=> XAssert.True(RecipeShape.Duplicates([new Ingredient(Wood, 1)], Wood, 1));

	[Fact]
	public void Two_into_one_is_compression_and_not_a_loop()
		=> XAssert.False(RecipeShape.Duplicates([new Ingredient(Wood, 2)], Wood, 1));

	[Fact]
	public void A_second_ingredient_pays_for_the_craft()
		=> XAssert.False(RecipeShape.Duplicates(
			[new Ingredient(Wood, 1), new Ingredient(Stone, 1)], Wood, 2));

	[Fact]
	public void A_different_item_is_not_a_loop()
		=> XAssert.False(RecipeShape.Duplicates([new Ingredient(Stone, 1)], Wood, 1));

	[Fact]
	public void A_group_that_accepts_the_result_makes_a_loop_of_another_item()
		// The accidental case: one group, written once and reused, that happens
		// to hold both the ingredient and what the recipe makes.
		=> XAssert.True(RecipeShape.Duplicates(
			[new Ingredient(Stone, 1)], Wood, 1, new HashSet<int> { Stone, Wood }));

	[Fact]
	public void A_group_that_does_not_hold_the_result_is_not_a_loop()
		=> XAssert.False(RecipeShape.Duplicates(
			[new Ingredient(Stone, 1)], Wood, 1, new HashSet<int> { Stone, 3 }));

	[Fact]
	public void A_group_does_not_excuse_a_smaller_result()
		=> XAssert.False(RecipeShape.Duplicates(
			[new Ingredient(Stone, 5)], Wood, 1, new HashSet<int> { Stone, Wood }));

	[Fact]
	public void A_recipe_with_no_ingredients_is_not_this_rule_s_business()
		// The loader refuses one of these, so the rule only has to not claim it.
		=> XAssert.False(RecipeShape.Duplicates([], Wood, 1));

	[Fact]
	public void Ingredients_are_required()
		=> XAssert.Throws<ArgumentNullException>(() => RecipeShape.Duplicates(null!, Wood, 1));
}
