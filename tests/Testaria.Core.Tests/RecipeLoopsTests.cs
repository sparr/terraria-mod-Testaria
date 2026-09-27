using System.Numerics;

namespace Testaria.Tests;

/// <summary>
/// <see cref="RecipeLoops"/>, which is the multi-recipe half of the free-output
/// question. The sweep over it cannot be self-tested, since every sweep skips
/// mods whose name ends in Test, so the loops that must be found and the loops
/// that must not be are shown here.
/// </summary>
public class RecipeLoopsTests
{
	private const int Wood = 9;
	private const int Stick = 2;
	private const int Stone = 1;
	private const int Sword = 4;

	private static RecipeStep Step(string name, int from, int consumes, int to, int produces)
		=> new(name, to, produces, [new RecipeSlot(consumes, new HashSet<int> { from })]);

	[Fact]
	public void A_round_trip_that_returns_more_than_it_took_is_a_loop()
	{
		RecipeLoopSearch search = RecipeLoops.Find([
			Step("sticks", Wood, 1, Stick, 4),
			Step("planks", Stick, 2, Wood, 1),
		]);

		RecipeLoop loop = XAssert.Single(search.Loops);

		XAssert.True(search.Complete);
		// One wood becomes four sticks becomes two wood.
		XAssert.Equal(new BigInteger(2), loop.GainNumerator);
		XAssert.Equal(BigInteger.One, loop.GainDenominator);
		XAssert.Equal([Stick, Wood], loop.Items.Order().ToArray());
		XAssert.Empty(loop.NewItems);
	}

	[Fact]
	public void A_reversible_conversion_is_not_a_loop()
		// Coins, and everything shaped like them. This is the case that decides
		// whether the check can be turned on at all: break-even conversions are
		// everywhere and none of them is a defect.
		=> XAssert.Empty(RecipeLoops.Find([
			Step("up", Wood, 100, Stick, 1),
			Step("down", Stick, 1, Wood, 100),
		]).Loops);

	[Fact]
	public void A_round_trip_that_loses_material_is_not_a_loop()
		=> XAssert.Empty(RecipeLoops.Find([
			Step("sticks", Wood, 1, Stick, 3),
			Step("planks", Stick, 4, Wood, 1),
		]).Loops);

	[Fact]
	public void A_recipe_crafting_its_own_ingredient_is_left_to_the_other_check()
		// One recipe is not a loop of recipes, and reporting it twice under two
		// names would make one defect look like two.
		=> XAssert.Empty(RecipeLoops.Find([Step("more wood", Wood, 1, Wood, 2)]).Loops);

	[Fact]
	public void A_loop_of_three_is_found()
	{
		RecipeLoop loop = XAssert.Single(RecipeLoops.Find([
			Step("a", Wood, 1, Stick, 2),
			Step("b", Stick, 1, Stone, 2),
			Step("c", Stone, 1, Wood, 2),
		]).Loops);

		XAssert.Equal(3, loop.Recipes.Count);
		XAssert.Equal(new BigInteger(8), loop.GainNumerator);
	}

	[Fact]
	public void A_loop_is_reported_once_however_many_ways_round_it_starts()
		=> XAssert.Single(RecipeLoops.Find([
			Step("a", Wood, 1, Stick, 2),
			Step("b", Stick, 1, Stone, 2),
			Step("c", Stone, 1, Wood, 2),
		]).Loops);

	[Fact]
	public void An_item_only_the_loop_produces_is_a_new_item_rather_than_an_input()
	{
		// The distinction the report has to make: Wood and Stick come back from
		// the loop itself, and the Sword is a different item that is free only
		// because one of them is unlimited.
		RecipeLoop loop = XAssert.Single(RecipeLoops.Find([
			Step("sticks", Wood, 1, Stick, 4),
			Step("planks", Stick, 2, Wood, 1),
			Step("sword", Stick, 10, Sword, 1),
		]).Loops);

		XAssert.Equal([Stick, Wood], loop.Items.Order().ToArray());
		XAssert.Equal([Sword], loop.NewItems);
	}

	[Fact]
	public void An_item_needing_something_the_loop_does_not_make_is_not_free()
	{
		RecipeLoop loop = XAssert.Single(RecipeLoops.Find([
			Step("sticks", Wood, 1, Stick, 4),
			Step("planks", Stick, 2, Wood, 1),
			new RecipeStep("sword", Sword, 1, [
				new RecipeSlot(10, new HashSet<int> { Stick }),
				new RecipeSlot(1, new HashSet<int> { Stone }),
			]),
		]).Loops);

		XAssert.Empty(loop.NewItems);
	}

	[Fact]
	public void A_loop_that_exists_only_through_a_recipe_group_is_found()
	{
		// A group is written once and reused, which is how a loop arrives by
		// accident: this one converts stone, and accepts wood for it.
		RecipeLoopSearch search = RecipeLoops.Find([
			Step("sticks", Wood, 1, Stick, 4),
			new RecipeStep("planks", Wood, 1, [
				new RecipeSlot(2, new HashSet<int> { Stone, Stick }),
			]),
		]);

		RecipeLoop loop = XAssert.Single(search.Loops);

		XAssert.Contains(Stick, loop.Items);
	}

	[Fact]
	public void A_recipe_with_two_ingredients_is_not_an_edge()
		// The second ingredient is consumed, so a loop through it is not free,
		// and deciding whether that ingredient is itself free is a different
		// question than a cycle in a graph.
		=> XAssert.Empty(RecipeLoops.Find([
			new RecipeStep("sticks", Stick, 4, [
				new RecipeSlot(1, new HashSet<int> { Wood }),
				new RecipeSlot(1, new HashSet<int> { Stone }),
			]),
			Step("planks", Stick, 2, Wood, 1),
		]).Loops);

	[Fact]
	public void A_search_that_runs_out_of_budget_says_so()
	{
		RecipeLoopSearch search = RecipeLoops.Find([
			Step("sticks", Wood, 1, Stick, 4),
			Step("planks", Stick, 2, Wood, 1),
		], budget: 1);

		XAssert.False(search.Complete);
	}

	[Fact]
	public void A_loop_longer_than_the_limit_is_not_searched_for()
		// Recorded as a limit rather than left to be discovered: the length is a
		// bound on the search, not a claim about recipes.
		=> XAssert.Empty(RecipeLoops.Find([
			Step("a", Wood, 1, Stick, 2),
			Step("b", Stick, 1, Stone, 2),
			Step("c", Stone, 1, Wood, 2),
		], maxLength: 2).Loops);

	[Fact]
	public void Free_items_close_over_everything_reachable()
		// Sorted by type, which is what the search returns: Stone 1, Stick 2,
		// Sword 4, Wood 9.
		=> XAssert.Equal(
			[Stone, Stick, Sword, Wood],
			RecipeLoops.FreeFrom([
				Step("sword", Stick, 1, Sword, 1),
				Step("stone", Sword, 1, Stone, 1),
			], [Wood, Stick]).ToArray());
}
