using Terraria;
using Terraria.Utilities;
using Testaria;

namespace TestariaSelfTest;

/// <summary>
/// That the game's randomness really is pinned, asserted against the game's
/// own generator rather than against Testaria's bookkeeping.
/// <para/>
/// A Tier 1 body runs synchronously the moment the runner starts the test,
/// with no tick in between, so its first draw is genuinely the first draw
/// after the reseed. That is what makes these assertions exact rather than
/// approximate: a Tier 2 body resumes a tick later, by which point the world
/// has drawn from the same generator on its own account.
/// </summary>
public class SeedTests
{
	[LoadedTest]
	[Seed(4242)]
	public void Randomness_starts_from_the_declared_seed()
	{
		// Reproducing the generator rather than recording a value in a static
		// keeps this test independent of whether, or in what order, any other
		// test ran.
		var expected = new UnifiedRandom(4242);

		Assert.Equal(expected.Next(1_000_000), Main.rand.Next(1_000_000));
		Assert.Equal(expected.Next(1_000_000), Main.rand.Next(1_000_000));
	}

	[LoadedTest]
	[Seed(4242)]
	public void The_declared_seed_applies_to_every_test_that_declares_it()
	{
		// The same seed as the test above, and therefore the same first draw.
		// Two tests agreeing is what proves the reseed happens per test rather
		// than once per run.
		var expected = new UnifiedRandom(4242);

		Assert.Equal(expected.Next(1_000_000), Main.rand.Next(1_000_000));
	}

	[LoadedTest]
	[Seed(99)]
	public void A_different_seed_is_a_different_stream()
	{
		var declared = new UnifiedRandom(99);
		var other = new UnifiedRandom(4242);

		Assert.Equal(declared.Next(1_000_000), Main.rand.Next(1_000_000));
		Assert.NotEqual(other.Next(1_000_000), declared.Next(1_000_000));
	}

	[LoadedTest]
	public void World_generation_draws_from_the_same_generator()
		// Not a Testaria fact but a Terraria one, and the reason one reseed
		// covers both: on the 1.4.5 line WorldGen.genRand is a property
		// returning Main.rand. On 1.4.4 they were separate fields, so this
		// assertion is also the tripwire for that changing back.
		=> Assert.Same(Main.rand, Terraria.WorldGen.genRand);
}
