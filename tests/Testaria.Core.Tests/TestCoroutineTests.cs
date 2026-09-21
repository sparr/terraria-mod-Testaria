using System.Collections;

namespace Testaria.Tests;

public class TestCoroutineTests
{
	[Fact]
	public void An_empty_body_completes_on_the_first_tick()
	{
		var coroutine = new TestCoroutine(Empty());

		XAssert.False(coroutine.Step());
		XAssert.Equal(CoroutineState.Completed, coroutine.State);
		XAssert.Equal(1, coroutine.ElapsedTicks);

		static IEnumerator Empty() { yield break; }
	}

	[Fact]
	public void Yielding_null_resumes_on_the_next_tick()
	{
		int reached = 0;
		var coroutine = new TestCoroutine(Body());

		coroutine.Step();
		XAssert.Equal(1, reached);

		coroutine.Step();
		XAssert.Equal(2, reached);

		IEnumerator Body()
		{
			reached = 1;
			yield return null;
			reached = 2;
		}
	}

	[Fact]
	public void Wait_ticks_suspends_for_exactly_that_many_ticks()
	{
		bool resumed = false;
		var coroutine = new TestCoroutine(Body());

		for (int i = 0; i < 10; i++) {
			coroutine.Step();
			XAssert.False(resumed, $"resumed too early, after {i + 1} ticks");
		}

		coroutine.Step();
		XAssert.True(resumed);

		IEnumerator Body()
		{
			yield return Wait.Ticks(10);
			resumed = true;
		}
	}

	[Fact]
	public void Wait_seconds_converts_at_sixty_ticks_per_second()
	{
		var coroutine = new TestCoroutine(Body());
		coroutine.RunToCompletion();

		// One second of simulation is 60 ticks, plus the tick that starts the
		// body and the tick it finishes on.
		XAssert.Equal(CoroutineState.Completed, coroutine.State);
		XAssert.Equal(61, coroutine.ElapsedTicks);

		static IEnumerator Body() { yield return Wait.Seconds(1); }
	}

	[Fact]
	public void Wait_until_resumes_on_the_tick_the_condition_becomes_true()
	{
		int tick = 0;
		bool resumed = false;
		var coroutine = new TestCoroutine(Body());

		for (int i = 0; i < 5; i++) {
			tick++;
			coroutine.Step();
		}

		XAssert.False(resumed);

		tick = 99;
		coroutine.Step();
		XAssert.True(resumed);

		IEnumerator Body()
		{
			yield return Wait.Until(() => tick > 50);
			resumed = true;
		}
	}

	[Fact]
	public void Wait_until_an_already_true_condition_costs_no_tick()
	{
		var coroutine = new TestCoroutine(Body());

		XAssert.False(coroutine.Step());
		XAssert.Equal(CoroutineState.Completed, coroutine.State);
		XAssert.Equal(1, coroutine.ElapsedTicks);

		static IEnumerator Body() { yield return Wait.Until(() => true); }
	}

	[Fact]
	public void Wait_while_is_the_inverse_of_wait_until()
	{
		bool blocking = true;
		var coroutine = new TestCoroutine(Body());

		coroutine.Step();
		coroutine.Step();
		XAssert.Equal(CoroutineState.Running, coroutine.State);

		blocking = false;
		coroutine.Step();
		XAssert.Equal(CoroutineState.Completed, coroutine.State);

		IEnumerator Body() { yield return Wait.While(() => blocking); }
	}

	[Fact]
	public void A_nested_enumerator_runs_to_completion_before_the_caller_resumes()
	{
		List<string> log = [];
		var coroutine = new TestCoroutine(Outer());
		coroutine.RunToCompletion();

		XAssert.Equal(["outer-before", "inner-1", "inner-2", "outer-after"], log);

		IEnumerator Outer()
		{
			log.Add("outer-before");
			yield return Inner();
			log.Add("outer-after");
		}

		IEnumerator Inner()
		{
			log.Add("inner-1");
			yield return null;
			log.Add("inner-2");
		}
	}

	[Fact]
	public void Nested_enumerators_nest_arbitrarily_deep()
	{
		int depthReached = 0;
		var coroutine = new TestCoroutine(Level(0));

		XAssert.Equal(CoroutineState.Completed, coroutine.RunToCompletion());
		XAssert.Equal(5, depthReached);

		IEnumerator Level(int depth)
		{
			depthReached = Math.Max(depthReached, depth);

			if (depth < 5)
				yield return Level(depth + 1);

			yield return null;
		}
	}

	[Fact]
	public void An_assertion_failure_faults_the_coroutine_and_keeps_the_exception()
	{
		var coroutine = new TestCoroutine(Body());
		coroutine.RunToCompletion();

		XAssert.Equal(CoroutineState.Faulted, coroutine.State);
		XAssert.IsType<AssertionException>(coroutine.Exception);
		XAssert.Contains("zombie", coroutine.Exception!.Message);

		static IEnumerator Body()
		{
			yield return null;
			Assert.Fail("zombie should be dead");
		}
	}

	[Fact]
	public void A_non_assertion_throw_also_faults_but_is_distinguishable()
	{
		// The runner maps AssertionException to Failed and anything else to
		// Errored, so the type must survive.
		var coroutine = new TestCoroutine(Body());
		coroutine.RunToCompletion();

		XAssert.Equal(CoroutineState.Faulted, coroutine.State);
		XAssert.IsType<InvalidOperationException>(coroutine.Exception);

		static IEnumerator Body()
		{
			yield return null;
			throw new InvalidOperationException("broken test");
		}
	}

	[Fact]
	public void A_throw_on_the_very_first_step_is_caught()
	{
		var coroutine = new TestCoroutine(Body());

		XAssert.False(coroutine.Step());
		XAssert.IsType<NotSupportedException>(coroutine.Exception);

		static IEnumerator Body()
		{
			throw new NotSupportedException();
#pragma warning disable CS0162
			yield break;
#pragma warning restore CS0162
		}
	}

	[Fact]
	public void A_body_that_runs_past_its_budget_times_out()
	{
		var coroutine = new TestCoroutine(Forever(), timeoutTicks: 30);
		coroutine.RunToCompletion();

		XAssert.Equal(CoroutineState.TimedOut, coroutine.State);
		XAssert.Equal(31, coroutine.ElapsedTicks);

		static IEnumerator Forever()
		{
			while (true)
				yield return null;
		}
	}

	[Fact]
	public void A_timeout_message_names_what_the_body_was_blocked_on()
	{
		// "timed out" alone is close to useless when debugging; knowing it was
		// stuck on a Wait.Until is the whole diagnosis.
		var coroutine = new TestCoroutine(Body(), timeoutTicks: 10);
		coroutine.RunToCompletion();

		XAssert.Contains("Wait.Until", coroutine.Exception!.Message);

		static IEnumerator Body() { yield return Wait.Until(() => false); }
	}

	[Fact]
	public void A_body_finishing_exactly_on_budget_is_not_a_timeout()
	{
		var coroutine = new TestCoroutine(Body(), timeoutTicks: 5);
		coroutine.RunToCompletion();

		XAssert.Equal(CoroutineState.Completed, coroutine.State);

		static IEnumerator Body() { yield return Wait.Ticks(4); }
	}

	[Fact]
	public void Zero_timeout_means_no_budget()
	{
		var coroutine = new TestCoroutine(Body(), timeoutTicks: 0);
		coroutine.RunToCompletion();

		XAssert.Equal(CoroutineState.Completed, coroutine.State);

		static IEnumerator Body() { yield return Wait.Ticks(5000); }
	}

	[Fact]
	public void A_body_that_spins_without_consuming_a_tick_faults_instead_of_hanging()
	{
		// Zero-tick waits in a loop would otherwise spin inside one tick and
		// hang the server with no diagnostic at all.
		var coroutine = new TestCoroutine(Spin());

		XAssert.False(coroutine.Step());
		XAssert.Equal(CoroutineState.Faulted, coroutine.State);
		XAssert.Contains("without yielding a wait that takes time", coroutine.Exception!.Message);

		static IEnumerator Spin()
		{
			while (true)
				yield return Wait.Ticks(0);
		}
	}

	[Fact]
	public void Yielding_something_meaningless_faults_with_an_explanation()
	{
		var coroutine = new TestCoroutine(Body());
		coroutine.RunToCompletion();

		XAssert.Equal(CoroutineState.Faulted, coroutine.State);
		XAssert.Contains("Yield null for the next tick", coroutine.Exception!.Message);

		static IEnumerator Body() { yield return 42; }
	}

	[Fact]
	public void Stepping_a_finished_coroutine_is_a_no_op()
	{
		var coroutine = new TestCoroutine(Empty());
		coroutine.RunToCompletion();

		int elapsed = coroutine.ElapsedTicks;

		XAssert.False(coroutine.Step());
		XAssert.Equal(elapsed, coroutine.ElapsedTicks);

		static IEnumerator Empty() { yield break; }
	}

	[Fact]
	public void A_negative_timeout_is_rejected()
		=> XAssert.Throws<ArgumentOutOfRangeException>(() => new TestCoroutine(Empty(), -1));

	[Fact]
	public void A_negative_wait_is_rejected()
		=> XAssert.Throws<ArgumentOutOfRangeException>(() => Wait.Ticks(-1));

	[Fact]
	public void A_null_predicate_is_rejected()
	{
		XAssert.Throws<ArgumentNullException>(() => Wait.Until(null!));
		XAssert.Throws<ArgumentNullException>(() => Wait.While(null!));
	}

	[Fact]
	public void A_null_body_is_rejected()
		=> XAssert.Throws<ArgumentNullException>(() => new TestCoroutine(null!));

	private static IEnumerator Empty() { yield break; }
}
