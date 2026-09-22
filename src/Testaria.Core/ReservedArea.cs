namespace Testaria;

/// <summary>
/// Ground the arena must never hand to a test.
/// <para/>
/// A blank world still has to be a *valid* world: vanilla code assumes a spawn
/// point exists, and a dungeon, and so on. Rather than scatter those through
/// the test ground, they go in named reserved areas that are simply never
/// leased. A test that genuinely wants to exercise the dungeon has to ask for
/// it by name, which makes "this test depends on vanilla furniture" a visible
/// declaration rather than an accident of where the arena happened to place a
/// box.
/// </summary>
public sealed record ReservedArea
{
	/// <summary>Name a test uses to ask for this area explicitly.</summary>
	public required string Name { get; init; }

	/// <summary>The ground held back.</summary>
	public required TileRect Bounds { get; init; }

	/// <summary>
	/// Why it exists, surfaced in diagnostics so that someone finding the
	/// arena short of room can see what is holding the space and why.
	/// </summary>
	public string? Reason { get; init; }
}
