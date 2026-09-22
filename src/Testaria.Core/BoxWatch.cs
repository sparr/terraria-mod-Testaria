namespace Testaria;

/// <summary>
/// Watches a box for things that should not be in it, and for things that
/// should be but have wandered out.
/// <para/>
/// Pure geometry and set membership, so the part most likely to be wrong is
/// the part most easily tested. The game layer supplies the entity positions.
/// <para/>
/// Note the cost asymmetry that shapes the design. Asking "did my entities
/// leave" is cheap, since a test's own list is short. Asking "did anything
/// arrive" means considering every active entity, which is why the pools being
/// small and fixed matters: a few thousand position checks a tick is
/// affordable, and nothing cheaper would actually answer the question.
/// </summary>
public static class BoxWatch
{
	/// <summary>
	/// Entities inside the box that the test does not own.
	/// <para/>
	/// A blank world removes almost every source of these, which is a stronger
	/// guarantee than watching, because it cannot race. This exists for the
	/// cases a blank world cannot cover: a neighbouring test's escapee, or
	/// anything the game spawns on its own.
	/// </summary>
	public static IEnumerable<int> FindIntruders(
		TileRect interior,
		IEnumerable<(int Id, WorldPoint Position)> entities,
		IReadOnlySet<int> owned)
	{
		ArgumentNullException.ThrowIfNull(entities);
		ArgumentNullException.ThrowIfNull(owned);

		foreach ((int id, WorldPoint position) in entities) {
			if (!owned.Contains(id) && BoxSpace.Contains(interior, position))
				yield return id;
		}
	}

	/// <summary>
	/// Entities the test owns that are no longer inside its box.
	/// <para/>
	/// An escape is reported rather than silently corrected, because the
	/// alternative is a neighbouring test failing for reasons nothing explains.
	/// Knowing which test let something out is most of the diagnosis.
	/// </summary>
	public static IEnumerable<int> FindEscapees(
		TileRect interior,
		IEnumerable<(int Id, WorldPoint Position)> entities,
		IReadOnlySet<int> owned)
	{
		ArgumentNullException.ThrowIfNull(entities);
		ArgumentNullException.ThrowIfNull(owned);

		foreach ((int id, WorldPoint position) in entities) {
			if (owned.Contains(id) && !BoxSpace.Contains(interior, position))
				yield return id;
		}
	}
}

/// <summary>
/// A context that notices when its box was not its own.
/// <para/>
/// Checked by the runner once a test finishes. Contamination is reported as an
/// <see cref="TestOutcome.Errored"/> rather than a failure: a failure says the
/// subject is broken, an error says the test did not run properly, and a box
/// something else was in is exactly the latter. Passing would be worse than
/// either, claiming coverage that did not happen.
/// </summary>
public interface IContaminationAware : ITestContext
{
	/// <summary>
	/// What was seen that should not have been, in the order noticed. Empty
	/// when the box stayed the test's own.
	/// </summary>
	IReadOnlyList<string> Contamination { get; }
}
