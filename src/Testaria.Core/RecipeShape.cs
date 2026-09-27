namespace Testaria;

/// <summary>
/// One ingredient of a recipe, as the only two numbers the rule below needs.
/// </summary>
/// <param name="Type">The item type required.</param>
/// <param name="Stack">How many of it are consumed.</param>
public readonly record struct Ingredient(int Type, int Stack);

/// <summary>
/// What can be said about a recipe's shape without knowing what it is for.
/// <para/>
/// A pure function, in the core, on purpose. The sweep that uses it needs a
/// loaded game and a mod that has the defect, and the framework's own mods are
/// invisible to every sweep by name, so an in-game test of the rule cannot be
/// written here. Lifting the decision out of the game is what makes it testable
/// at tier 0 instead, which is the answer the withdrawn ID set check wanted and
/// never got.
/// </summary>
public static class RecipeShape
{
	/// <summary>
	/// Whether a recipe crafts its own ingredient in at least the quantity it
	/// consumes, which is an item the world can make more of for free.
	/// <para/>
	/// Three conditions, and each excludes a legitimate recipe:
	/// <list type="bullet">
	/// <item>One ingredient. A second is consumed too, so the loop costs
	/// something.</item>
	/// <item>The result's own type, either as the ingredient itself or through a
	/// recipe group that accepts it. The group case is how this usually arrives
	/// by accident, since a group is written once and reused.</item>
	/// <item>A result stack no smaller than the ingredient stack. Smaller is a
	/// compression recipe, which is the useful inverse of this and must not be
	/// reported.</item>
	/// </list>
	/// </summary>
	/// <param name="ingredients">The recipe's ingredients.</param>
	/// <param name="resultType">The item type the recipe produces.</param>
	/// <param name="resultStack">How many of it one craft yields.</param>
	/// <param name="acceptedTypes">
	/// Every item type the recipe's groups accept, or empty when it has none.
	/// </param>
	public static bool Duplicates(
		IReadOnlyList<Ingredient> ingredients,
		int resultType,
		int resultStack,
		IReadOnlySet<int>? acceptedTypes = null)
	{
		ArgumentNullException.ThrowIfNull(ingredients);

		if (ingredients.Count != 1)
			return false;

		Ingredient only = ingredients[0];
		bool sameItem = only.Type == resultType || (acceptedTypes?.Contains(resultType) ?? false);

		return sameItem && resultStack >= only.Stack;
	}
}
