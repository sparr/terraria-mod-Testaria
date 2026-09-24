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
/// A test that runs inside a box the arena leases it.
/// <para/>
/// Separate from the tier, because more than one tier wants ground of its own:
/// a tier 2 test and a tier 3 test both edit tiles, and both need those edits
/// to be somewhere nobody else is looking.
/// </summary>
public interface IBoxedTest
{
	/// <summary>Builds the arena request this test describes.</summary>
	BoxRequest ToRequest();
}

/// <summary>
/// A test needing a world and a tick loop, run inside a leased box.
/// <para/>
/// Declare <see cref="Spans"/> when the subject of the test is a band
/// boundary itself, such as falling from the surface into the cavern. Height
/// is then dictated by the world rather than by the author, so it is ignored.
/// </summary>
public sealed class GameTestAttribute : TestariaTestAttribute, IBoxedTest
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

	/// <summary>
	/// Requested interior width in tiles, before size class rounding.
	/// <para/>
	/// Measured rather than guessed (PLAN.md section 8.5f): across 33 boxed
	/// tests, the furthest any test's own entities ranged was 25 tiles wide by
	/// 21 tall, and the largest patch of ground any of them changed was 5 by 3.
	/// So 48 by 32, the smallest size class, covers everything observed with
	/// room to spare, where the previous 80 by 48 rounded up to a box four
	/// times the area of anything anyone used.
	/// <para/>
	/// The corpus has no boss fights and nothing that teleports, which are
	/// exactly the tests that would want more. Ask for more when you need it;
	/// that is what the property is for.
	/// </summary>
	public int Width { get; set; } = 48;

	/// <summary>
	/// Requested interior height in tiles, before size class rounding. Ignored
	/// for a spanning box.
	/// </summary>
	public int Height { get; set; } = 32;

	/// <summary>Builds the arena request this attribute describes.</summary>
	public BoxRequest ToRequest()
		=> Spans == Band.None
			? BoxRequest.Banded(Band, Width, Height)
			: BoxRequest.Spanning(Spans, Width);
}

/// <summary>
/// A test needing a server and at least one connected client.
/// <para/>
/// Netcode, sync, packet round trips, and anything whose subject is the
/// difference between what the two processes believe. The body runs on the
/// server, which owns the run; the client is a puppet that answers questions
/// about what it can see. A test declared here is reported as skipped rather
/// than run when no client is connected, because a netcode test that quietly
/// runs single-player proves nothing while reporting a pass.
/// </summary>
public sealed class NetTestAttribute : TestariaTestAttribute, IBoxedTest
{
	/// <summary>Marks a Tier 3 test.</summary>
	public NetTestAttribute() : base(TestTier.MultiProcess) { }

	/// <summary>The single band the box sits in. Ignored when <see cref="Spans"/> is set.</summary>
	public Band Band { get; set; } = Band.Surface;

	/// <summary>Bands the box must cross.</summary>
	public Band Spans { get; set; } = Band.None;

	/// <summary>Requested interior width in tiles, before size class rounding.</summary>
	public int Width { get; set; } = 48;

	/// <summary>Requested interior height in tiles, before size class rounding.</summary>
	public int Height { get; set; } = 32;

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
