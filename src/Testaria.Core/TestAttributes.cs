namespace Testaria;

/// <summary>
/// Base for every Testaria test marker. Not used directly; pick the
/// attribute matching the tier the test actually needs.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public abstract class TestariaTestAttribute : Attribute
{
	/// <param name="tier">The environment this test needs.</param>
	protected TestariaTestAttribute(TestTier tier) => Tier = tier;

	/// <summary>The environment this test needs.</summary>
	public TestTier Tier { get; }

	/// <summary>
	/// When set, the test is reported as skipped with this reason rather than
	/// run. Reported rather than omitted, so that a suite never silently
	/// shrinks.
	/// </summary>
	public string? Skip { get; set; }

	/// <summary>
	/// Ticks the test body may run before it is abandoned. Zero means the
	/// runner's default. Measured in ticks rather than seconds because the
	/// simulation is what is being timed, not the wall clock.
	/// </summary>
	public int Timeout { get; set; }
}

/// <summary>
/// A test needing a completed load pass but no world. Content registration,
/// recipes, ID sets, config serialization, localization coverage.
/// </summary>
public sealed class LoadedTestAttribute : TestariaTestAttribute
{
	/// <summary>Marks a Tier 1 test.</summary>
	public LoadedTestAttribute() : base(TestTier.Loaded) { }
}

/// <summary>
/// A test needing a world and a tick loop, run inside a leased box.
/// <para/>
/// Declare <see cref="Spans"/> when the subject of the test is a band
/// boundary itself, such as falling from the surface into the cavern. Height
/// is then dictated by the world rather than by the author, so it is ignored.
/// </summary>
public sealed class GameTestAttribute : TestariaTestAttribute
{
	/// <summary>Marks a Tier 2 test.</summary>
	public GameTestAttribute() : base(TestTier.World) { }

	/// <summary>The single band the box sits in. Ignored when <see cref="Spans"/> is set.</summary>
	public Band Band { get; set; } = Band.Surface;

	/// <summary>
	/// Bands the box must cross. Gaps are filled, since a column cannot
	/// physically skip a band.
	/// </summary>
	public Band Spans { get; set; } = Band.None;

	/// <summary>Requested interior width in tiles, before size class rounding.</summary>
	public int Width { get; set; } = 80;

	/// <summary>
	/// Requested interior height in tiles, before size class rounding. Ignored
	/// for a spanning box.
	/// </summary>
	public int Height { get; set; } = 48;

	/// <summary>Builds the arena request this attribute describes.</summary>
	public BoxRequest ToRequest()
		=> Spans == Band.None
			? BoxRequest.Banded(Band, Width, Height)
			: BoxRequest.Spanning(Spans, Width);
}

/// <summary>
/// Runs the test in a freshly generated world rather than a leased box.
/// <para/>
/// The escape hatch for tests that a box cannot isolate, such as anything
/// asserting on world-global state. Correct but slow, since it costs a world
/// generation, so it is opt-in.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class FreshWorldAttribute : Attribute;

/// <summary>
/// Pins this test's randomness to a particular seed instead of the one
/// derived from its name.
/// <para/>
/// Every test is seeded, so this is not how a test becomes deterministic, it
/// is how a test asks for a *specific* roll: the seed that reproduced a bug,
/// or one chosen because it happens to make a rare branch happen. Written
/// down in the test rather than in a comment, it survives being reported and
/// rerun.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SeedAttribute : Attribute
{
	/// <param name="seed">The seed this test's generators start from.</param>
	public SeedAttribute(int seed) => Seed = seed;

	/// <summary>The seed this test's generators start from.</summary>
	public int Seed { get; }
}

/// <summary>
/// Exempts a test from the run's fast forward, running it at the game's own
/// 60 ticks per second.
/// <para/>
/// For tests whose subject is wall clock rather than simulated time: anything
/// measuring real elapsed duration, waiting on a background task, or talking
/// to something outside the tick loop. Simulated behaviour is unaffected by
/// fast forward, so an ordinary gameplay test never needs this.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RealTimeAttribute : Attribute;

/// <summary>
/// Freezes the world as the test begins, so nothing moves until the test asks
/// it to with <see cref="TestContextSteppingExtensions.Step"/>.
/// <para/>
/// The tick the body starts on still happens, because the body has to run in
/// order to ask for anything; the freeze takes effect from the tick after. So
/// this is exactly equivalent to calling
/// <see cref="TestContextSteppingExtensions.Pause"/> as the first statement,
/// and exists so the intent is visible in the test list.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class StartPausedAttribute : Attribute;
