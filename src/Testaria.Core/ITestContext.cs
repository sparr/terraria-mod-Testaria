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
