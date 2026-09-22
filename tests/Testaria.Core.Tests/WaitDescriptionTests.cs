using System.Collections;

namespace Testaria.Tests;

/// <summary>
/// A wait's description exists for exactly one reader: whoever is looking at a
/// timeout message trying to work out what the test was stuck on.
/// </summary>
public class WaitDescriptionTests
{
	[Fact]
	public void A_described_wait_names_what_it_is_waiting_for()
	{
		XAssert.Equal("Wait.Until(the boss is dead)", Wait.Until(() => false, "the boss is dead").ToString());
		XAssert.Equal("Wait.While(it is still falling)", Wait.While(() => true, "it is still falling").ToString());
	}

	[Fact]
	public void An_undescribed_wait_still_reads_sensibly()
	{
		XAssert.Equal("Wait.Until(...)", Wait.Until(() => false).ToString());
		XAssert.Equal("Wait.While(...)", Wait.While(() => true).ToString());
	}

	[Fact]
	public void The_description_reaches_the_timeout_message()
	{
		var coroutine = new TestCoroutine(Body(), timeoutTicks: 3);
		coroutine.RunToCompletion();

		XAssert.Equal(CoroutineState.TimedOut, coroutine.State);
		XAssert.Contains("Wait.Until(the boss is dead)", coroutine.Exception!.Message);

		static IEnumerator Body()
		{
			yield return Wait.Until(() => false, "the boss is dead");
		}
	}

	[Fact]
	public void Describing_a_wait_does_not_change_when_it_resumes()
	{
		bool ready = false;
		var coroutine = new TestCoroutine(Body());

		coroutine.Step();
		coroutine.Step();
		XAssert.Equal(CoroutineState.Running, coroutine.State);

		ready = true;
		coroutine.Step();
		XAssert.Equal(CoroutineState.Completed, coroutine.State);

		IEnumerator Body()
		{
			yield return Wait.Until(() => ready, "ready");
		}
	}
}
