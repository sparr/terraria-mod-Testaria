namespace Testaria;

/// <summary>
/// Works out which seed a test's randomness starts from.
/// <para/>
/// Terraria drives an enormous amount of behaviour through <c>Main.rand</c>
/// and <c>WorldGen.genRand</c>: damage variance, critical hits, AI decisions,
/// drop rolls, spawn choices, ore placement. Left alone, a gameplay test is a
/// dice roll that usually lands the same way, which is the definition of a
/// flaky suite. Seeding per test makes a failure reproducible, which is the
/// difference between a bug report and a shrug.
/// <para/>
/// Per test, not per run, and derived from the test's own identity rather than
/// from a counter. A counter would make a test's seed depend on how many tests
/// ran before it, so filtering a suite down to the one failing test would hand
/// it a different seed and, quite possibly, a pass.
/// </summary>
public static class TestSeed
{
	// FNV-1a. Hand-rolled rather than string.GetHashCode, which is randomized
	// per process by design: seeds from it would differ between two runs of
	// the same suite on the same machine, which is the exact property this
	// type exists to provide.
	private const ulong FnvOffset = 14695981039346656037;
	private const ulong FnvPrime = 1099511628211;

	/// <summary>
	/// The seed for one test, stable across processes, machines, and runs.
	/// </summary>
	/// <param name="runSeed">The run's own seed, which shifts every test at once.</param>
	/// <param name="className">The test's declaring type.</param>
	/// <param name="name">The test's name, including its case arguments.</param>
	public static int For(int runSeed, string className, string name)
	{
		ArgumentNullException.ThrowIfNull(className);
		ArgumentNullException.ThrowIfNull(name);

		ulong hash = FnvOffset;

		Absorb(ref hash, className);
		Absorb(ref hash, ".");
		Absorb(ref hash, name);

		// The run seed goes in last and whole, so that two runs of the same
		// suite at different run seeds share no per-test seeds at all.
		unchecked {
			hash ^= (ulong)(uint)runSeed;
			hash *= FnvPrime;
		}

		return Fold(hash);
	}

	private static void Absorb(ref ulong hash, string text)
	{
		unchecked {
			foreach (char c in text) {
				hash ^= c;
				hash *= FnvPrime;
			}
		}
	}

	// Non-negative, because a seed is conventionally read as one and a
	// negative value in a report reads like a bug rather than a value.
	private static int Fold(ulong hash) => (int)((hash ^ (hash >> 32)) & int.MaxValue);
}
