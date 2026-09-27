using System.Numerics;

namespace Testaria;

/// <summary>
/// One ingredient slot of a recipe: how many are consumed, and every item type
/// that can satisfy it, which is the declared type plus whatever a recipe group
/// lets stand in for it.
/// </summary>
/// <param name="Stack">How many the recipe consumes.</param>
/// <param name="Accepts">Every type that satisfies the slot, including the declared one.</param>
public readonly record struct RecipeSlot(int Stack, IReadOnlySet<int> Accepts);

/// <summary>A recipe, reduced to what a search for free output needs.</summary>
/// <param name="Name">How a report should refer to it.</param>
/// <param name="Result">The item type it produces.</param>
/// <param name="ResultStack">How many of that item one craft yields.</param>
/// <param name="Slots">Its ingredients.</param>
public sealed record RecipeStep(string Name, int Result, int ResultStack, IReadOnlyList<RecipeSlot> Slots);

/// <summary>
/// A loop of recipes that hands back more than it was given.
/// <para/>
/// The two item collections are the distinction that matters to whoever has to
/// fix it: <see cref="Items"/> is what the loop consumes and returns, so more of
/// an input, and <see cref="NewItems"/> is everything else that becomes free once
/// an input is unlimited, which is a different item entirely.
/// </summary>
/// <param name="Recipes">The recipes making up the loop, in the order crafted.</param>
/// <param name="Items">The item types the loop passes through, in the same order.</param>
/// <param name="GainNumerator">What one trip around returns, over what it took.</param>
/// <param name="GainDenominator">The other half of that ratio, reduced.</param>
/// <param name="NewItems">
/// Item types that are not in the loop and become free through it, sorted.
/// </param>
public sealed record RecipeLoop(
	IReadOnlyList<string> Recipes,
	IReadOnlyList<int> Items,
	BigInteger GainNumerator,
	BigInteger GainDenominator,
	IReadOnlyList<int> NewItems);

/// <summary>
/// What a search found, and whether it finished.
/// </summary>
/// <param name="Loops">Every loop found, each once.</param>
/// <param name="Complete">
/// False when the search hit its own limits and stopped, which means the absence
/// of a loop proves nothing. Reported rather than swallowed: a bounded search
/// that says nothing looks exactly like a clean one.
/// </param>
public sealed record RecipeLoopSearch(IReadOnlyList<RecipeLoop> Loops, bool Complete);

/// <summary>
/// Searches a set of recipes for loops that produce free output.
/// <para/>
/// <see cref="RecipeShape.Duplicates"/> asks this of one recipe. A loop is the
/// same defect spread over several: wood into sticks into wood, where the round
/// trip returns more wood than it took. Nobody writes that on purpose, which is
/// exactly why it survives review: each recipe is defensible on its own and only
/// the cycle is wrong.
/// <para/>
/// <b>Only recipes with a single ingredient slot form the graph.</b> A second
/// ingredient is consumed too, so a loop through it costs something real unless
/// that ingredient is itself free, and deciding <i>that</i> is a reachability
/// question over multisets rather than a cycle in a graph. The restriction is
/// what keeps this a search with an answer instead of a solver with a timeout,
/// and it is the same restriction that makes the arithmetic exact: one slot in,
/// one stack out, so a loop's yield is a product of ratios.
/// <para/>
/// A loop of one recipe is left to <see cref="RecipeShape.Duplicates"/>, which
/// reports it against the recipe itself rather than as a cycle.
/// <para/>
/// Pure, and in the core, for the same reason as the rest of this file: the sweep
/// that calls it cannot be self-tested, because every sweep excludes mods whose
/// name ends in Test, so a deliberately looped pair of recipes here would be
/// invisible to it.
/// </summary>
public static class RecipeLoops
{
	/// <summary>How many recipes a loop may be made of before the search stops looking.</summary>
	public const int DefaultMaxLength = 8;

	/// <summary>
	/// How many steps the search may take before giving up and saying so.
	/// <para/>
	/// Cycle enumeration is exponential in the worst case, and a recipe graph is
	/// somebody else's data. A budget with an honest report beats a hang.
	/// </summary>
	public const int DefaultBudget = 500_000;

	/// <summary>
	/// Every loop whose output exceeds its input, each found once.
	/// </summary>
	/// <param name="steps">The recipes to search, which may include vanilla's.</param>
	/// <param name="maxLength">How many recipes a loop may contain.</param>
	/// <param name="budget">How many search steps to spend before stopping.</param>
	public static RecipeLoopSearch Find(
		IReadOnlyList<RecipeStep> steps,
		int maxLength = DefaultMaxLength,
		int budget = DefaultBudget)
	{
		ArgumentNullException.ThrowIfNull(steps);

		var outgoing = new Dictionary<int, List<Edge>>();

		foreach (RecipeStep step in steps) {
			// One slot only, and the reason is in the class summary.
			if (step.Slots.Count != 1)
				continue;

			RecipeSlot slot = step.Slots[0];

			if (slot.Stack <= 0 || step.ResultStack <= 0)
				continue;

			foreach (int from in slot.Accepts) {
				if (!outgoing.TryGetValue(from, out List<Edge>? edges))
					outgoing[from] = edges = [];

				edges.Add(new Edge(step.Name, from, step.Result, slot.Stack, step.ResultStack));
			}
		}

		var search = new Search(outgoing, steps, maxLength, budget);

		foreach (int start in outgoing.Keys.Order())
			search.From(start);

		return new RecipeLoopSearch(search.Found, search.Complete);
	}

	/// <summary>
	/// Everything that becomes free once these items are unlimited: any recipe all
	/// of whose slots can be satisfied from what is already free produces another
	/// free item, and so on until nothing new appears.
	/// <para/>
	/// Stacks are ignored on purpose. An unlimited item is unlimited whatever the
	/// recipe asks for.
	/// </summary>
	public static IReadOnlyList<int> FreeFrom(IReadOnlyList<RecipeStep> steps, IEnumerable<int> unlimited)
	{
		ArgumentNullException.ThrowIfNull(steps);
		ArgumentNullException.ThrowIfNull(unlimited);

		HashSet<int> free = [.. unlimited];
		bool grew = true;

		while (grew) {
			grew = false;

			foreach (RecipeStep step in steps) {
				if (free.Contains(step.Result))
					continue;

				bool satisfied = true;

				foreach (RecipeSlot slot in step.Slots) {
					if (!slot.Accepts.Any(free.Contains)) {
						satisfied = false;
						break;
					}
				}

				// A recipe with no ingredients at all is free output without a
				// loop, which is a different claim and not this one's; it is not
				// treated as a source here.
				if (satisfied && step.Slots.Count > 0 && free.Add(step.Result))
					grew = true;
			}
		}

		return [.. free.Order()];
	}

	private readonly record struct Edge(string Name, int From, int To, int Consumes, int Produces);

	/// <summary>
	/// One run of the enumeration. A class rather than a pile of ref parameters,
	/// because the budget and the found set are shared by every branch.
	/// </summary>
	private sealed class Search(
		Dictionary<int, List<Edge>> outgoing,
		IReadOnlyList<RecipeStep> steps,
		int maxLength,
		int budget)
	{
		public List<RecipeLoop> Found { get; } = [];

		public bool Complete { get; private set; } = true;

		private readonly HashSet<string> seen = [];
		private readonly List<Edge> path = [];
		private readonly HashSet<int> onPath = [];
		private int left = budget;

		public void From(int start)
		{
			onPath.Clear();
			path.Clear();
			onPath.Add(start);

			Walk(start, start, BigInteger.One, BigInteger.One);
		}

		private void Walk(int start, int at, BigInteger produced, BigInteger consumed)
		{
			if (left-- <= 0) {
				Complete = false;
				return;
			}

			if (path.Count >= maxLength || !outgoing.TryGetValue(at, out List<Edge>? edges))
				return;

			foreach (Edge edge in edges) {
				BigInteger yields = produced * edge.Produces;
				BigInteger costs = consumed * edge.Consumes;

				if (edge.To == start) {
					// A single edge back to where it started is one recipe
					// crafting its own ingredient, which RecipeShape.Duplicates
					// reports against that recipe.
					if (path.Count > 0 && yields > costs)
						Record(edge, yields, costs);

					continue;
				}

				// Only nodes above the start, so each loop is enumerated once,
				// from its own lowest item, rather than once per rotation.
				if (edge.To < start || !onPath.Add(edge.To))
					continue;

				path.Add(edge);
				Walk(start, edge.To, yields, costs);
				path.RemoveAt(path.Count - 1);
				onPath.Remove(edge.To);
			}
		}

		private void Record(Edge closing, BigInteger yields, BigInteger costs)
		{
			string[] recipes = [.. path.Select(edge => edge.Name), closing.Name];
			int[] items = [.. path.Select(edge => edge.From), closing.From];
			string key = string.Join(
				" -> ",
				path.Select(edge => $"{edge.Name}:{edge.From}>{edge.To}")
					.Append($"{closing.Name}:{closing.From}>{closing.To}"));

			if (!seen.Add(key))
				return;

			BigInteger divisor = BigInteger.GreatestCommonDivisor(yields, costs);
			IReadOnlyList<int> free = FreeFrom(steps, items);

			Found.Add(new RecipeLoop(
				recipes,
				items,
				yields / divisor,
				costs / divisor,
				[.. free.Where(item => !items.Contains(item))]));
		}
	}
}
