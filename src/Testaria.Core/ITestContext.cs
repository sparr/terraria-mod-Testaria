namespace Testaria;

/// <summary>
/// What a running test can see of its own box.
/// <para/>
/// Deliberately minimal and game-free. The mod layer implements this with a
/// type that also offers spawning, ticking, and world manipulation; keeping
/// the interface itself free of game types is what lets the core validate a
/// test method's signature without referencing tModLoader.
/// </summary>
public interface ITestContext
{
	/// <summary>The usable area. A test spawns inside this.</summary>
	TileRect Interior { get; }

	/// <summary>The outer extent, gutter included.</summary>
	TileRect Bounds { get; }

	/// <summary>The band or bands the box occupies.</summary>
	Band Bands { get; }

	/// <summary>Ticks elapsed since the test body started.</summary>
	int ElapsedTicks { get; }
}

/// <summary>
/// A context that wants to be told when a tick passes.
/// <para/>
/// Separate from <see cref="ITestContext"/> because Tier 0 and Tier 1 contexts
/// have no tick loop to be told about, and an interface member they must
/// implement and ignore is worse than an interface they do not implement.
/// </summary>
public interface ITickingContext : ITestContext
{
	/// <summary>Called by the runner once per game tick while the test runs.</summary>
	void Tick();
}
