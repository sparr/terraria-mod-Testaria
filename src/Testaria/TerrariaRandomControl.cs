using Terraria;
using Terraria.Utilities;

namespace Testaria;

/// <summary>
/// Pins Terraria's randomness to a test's seed for as long as that test runs.
/// <para/>
/// There is exactly one generator to pin, which is worth knowing rather than
/// assuming: in 1.4.5 <c>WorldGen.genRand</c> is a property returning
/// <c>Main.rand</c> (checked in the decompiled 1.4.5.8 source and again in
/// tModLoader's own <c>WorldGen.cs.patch</c>), so world generation and
/// gameplay draw from the same stream. On the 1.4.4 line they were separate
/// fields, which is the kind of difference that silently halves the coverage
/// of a fix ported between branches.
/// </summary>
internal sealed class TerrariaRandomControl : IRandomControl
{
	/// <inheritdoc/>
	public void Reseed(int seed) => Apply(seed);

	/// <summary>
	/// Returns the game to rolling unpredictably.
	/// <para/>
	/// Not by restoring the stream the game was on, which cannot be done: the
	/// seed is set in place rather than by swapping the instance, precisely so
	/// that anything holding a cached reference to <c>Main.rand</c> is covered
	/// too, and an in-place generator has no old position to go back to.
	/// Reseeding from the clock is the honest equivalent, and nothing in the
	/// game depends on its randomness resuming where it left off.
	/// </summary>
	public void Restore() => Apply(Environment.TickCount);

	private static void Apply(int seed)
	{
		// Null before the game finishes starting. A run cannot begin that
		// early, but a guard here costs nothing and a NullReferenceException
		// from inside the runner would be read as a framework bug.
		if (Main.rand is UnifiedRandom random)
			random.SetSeed(seed);
	}
}
