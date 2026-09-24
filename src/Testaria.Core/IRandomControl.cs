namespace Testaria;

/// <summary>
/// Puts the game's random generators where a test can rely on them.
/// <para/>
/// An interface rather than direct calls into Terraria because the core
/// carries no tModLoader reference: the runner decides *when* a test's
/// randomness is pinned, and the game-facing half decides *what* pinning
/// means. It also lets the decision be tested without a game, which is the
/// half most likely to be wrong.
/// </summary>
public interface IRandomControl
{
	/// <summary>
	/// Restarts every generator the harness controls from this seed.
	/// </summary>
	/// <param name="seed">The test's seed.</param>
	void Reseed(int seed);

	/// <summary>
	/// Hands the generators the game started with back.
	/// <para/>
	/// Called however a test ends. A run that left the game holding a seeded
	/// generator would make everything after it, including the next run's
	/// world, quietly deterministic in a way nobody asked for.
	/// </summary>
	void Restore();
}
