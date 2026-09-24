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

/// <summary>
/// A context whose test can stop the world and walk it forward by hand.
/// <para/>
/// Separate from <see cref="ITestContext"/> for the same reason
/// <see cref="ITickingContext"/> is: only a tier with a running world has
/// anything to freeze, and an interface member the others must implement and
/// reject is worse than one they do not implement.
/// </summary>
public interface ISteppableContext : ITestContext
{
	/// <summary>True when the world is not advancing.</summary>
	bool IsPaused { get; }

	/// <summary>
	/// Stops the world from the next tick onwards. The current tick finishes
	/// first, since the test is running inside it.
	/// </summary>
	void Pause();

	/// <summary>Lets the world run freely again.</summary>
	void Resume();

	/// <summary>
	/// Lets the world advance exactly <paramref name="ticks"/> ticks and then
	/// freeze again, pausing first if it was running.
	/// </summary>
	/// <returns>
	/// A wait covering those ticks, so the natural use is
	/// <c>yield return ctx.Step(10);</c> and the body resumes on the last of
	/// them, in time to ask for more.
	/// </returns>
	Wait Step(int ticks = 1);
}

/// <summary>
/// Stepping, reached from the plain <see cref="ITestContext"/> a test is
/// handed.
/// <para/>
/// Extension methods rather than members so that a tier without a world says
/// so when asked, instead of every context carrying methods most of them
/// cannot honour.
/// </summary>
public static class TestContextSteppingExtensions
{
	/// <summary>True when the world is frozen. False for a context that cannot freeze it.</summary>
	public static bool IsPaused(this ITestContext context)
		=> context is ISteppableContext steppable && steppable.IsPaused;

	/// <summary>Stops the world from the next tick onwards.</summary>
	public static void Pause(this ITestContext context) => Steppable(context).Pause();

	/// <summary>Lets the world run freely again.</summary>
	public static void Resume(this ITestContext context) => Steppable(context).Resume();

	/// <summary>
	/// Advances the world by <paramref name="ticks"/> ticks and freezes again.
	/// Yield the result: <c>yield return ctx.Step(10);</c>
	/// </summary>
	public static Wait Step(this ITestContext context, int ticks = 1) => Steppable(context).Step(ticks);

	private static ISteppableContext Steppable(ITestContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return context as ISteppableContext
			?? throw new InvalidOperationException(
				$"This test's context ({context.GetType().Name}) cannot pause the world. Stopping and stepping the " +
				"simulation needs a tier that has one, so mark the test [GameTest] rather than [LoadedTest].");
	}
}
