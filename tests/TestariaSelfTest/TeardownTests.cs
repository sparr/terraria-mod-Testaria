using System.Collections;
using Testaria;

namespace TestariaSelfTest;

/// <summary>
/// That state a test registered for restoration really is restored.
/// <para/>
/// A box isolates a region of the world and nothing else. A test that changes
/// a static field has to put it back by hand, and doing it on the last line
/// stops happening the moment an assertion above it throws, which is exactly
/// when it matters most: the run carries on and every test after it inherits
/// the change.
/// <para/>
/// Each test here disposes its own context and then looks at what happened.
/// The alternative, leaving a mark for a later test to find, would depend on
/// tests running in the order they are written, which discovery does not
/// promise. Disposing twice is harmless: teardown clears every list it walks,
/// so the runner's own dispose afterwards finds nothing left to do.
/// </summary>
public class TeardownTests
{
	[GameTest(Band = Band.Cavern, Timeout = 300)]
	public IEnumerator A_registered_restoration_runs_at_teardown(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		int global = 1;

		box.Restore(() => global = 1);
		global = 2;

		Assert.Equal(2, global);

		box.Dispose();

		Assert.Equal(1, global);

		yield break;
	}

	[GameTest(Band = Band.Cavern, Timeout = 300)]
	public IEnumerator Change_reads_writes_and_registers_in_one_go(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		int global = 7;

		box.Change(() => global, value => global = value, 9);

		Assert.Equal(9, global);

		box.Dispose();

		Assert.Equal(7, global);

		yield break;
	}

	/// <summary>
	/// Reverse order, so that nesting unwinds the way a stack does: the last
	/// change made is the first one undone.
	/// </summary>
	[GameTest(Band = Band.Cavern, Timeout = 300)]
	public IEnumerator Restorations_unwind_in_reverse_order(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		List<string> order = [];

		box.Restore(() => order.Add("first"));
		box.Restore(() => order.Add("second"));

		box.Dispose();

		Assert.Equal(["second", "first"], order);

		yield break;
	}

	/// <summary>
	/// One restoration throwing must not strand the others. The whole reason
	/// to register them was to be sure they happen, and teardown is the one
	/// place where giving up half way is worse than carrying on.
	/// </summary>
	[GameTest(Band = Band.Cavern, Timeout = 300)]
	public IEnumerator A_restoration_that_throws_does_not_stop_the_rest(ITestContext ctx)
	{
		var box = (TestContext)ctx;
		bool ranAnyway = false;

		box.Restore(() => ranAnyway = true);
		box.Restore(() => throw new InvalidOperationException("deliberate"));

		box.Dispose();

		Assert.True(ranAnyway, "the earlier restoration should still have run");

		yield break;
	}

	/// <summary>
	/// Registering nothing is not an error, and teardown is not surprised by
	/// a test that asked for no restorations at all.
	/// </summary>
	[GameTest(Band = Band.Cavern, Timeout = 300)]
	public IEnumerator A_test_that_registers_nothing_tears_down_cleanly(ITestContext ctx)
	{
		var box = (TestContext)ctx;

		box.Dispose();

		// Asserting that the box is still describable is the smallest honest
		// claim available here, and the alternative, asserting nothing, now
		// earns a note saying the test proved nothing. Which it would have.
		Assert.False(box.Interior.IsEmpty, "the box should still know where it was");

		yield break;
	}
}
