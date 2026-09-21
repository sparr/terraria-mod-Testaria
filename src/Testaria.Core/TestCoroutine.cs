using System.Collections;

namespace Testaria;

/// <summary>Where a coroutine body has got to.</summary>
public enum CoroutineState
{
	/// <summary>Still going.</summary>
	Running,

	/// <summary>Ran to completion.</summary>
	Completed,

	/// <summary>Exceeded its tick budget and was abandoned.</summary>
	TimedOut,

	/// <summary>Threw. See <see cref="TestCoroutine.Exception"/>.</summary>
	Faulted,
}

/// <summary>
/// Drives an <see cref="IEnumerator"/> test body one step per game tick.
/// <para/>
/// Coroutines rather than async/await: a synchronization context would invite
/// accidental thread hops into code that is not thread safe, and Terraria's
/// tick loop is single threaded. A coroutine resumes exactly where the tick
/// loop says it may, and nowhere else.
/// <para/>
/// Nested enumerators are supported, so a test can factor setup into helper
/// coroutines and <c>yield return</c> them.
/// </summary>
public sealed class TestCoroutine
{
	/// <summary>
	/// Guards against a body that yields only zero-tick waits, which would
	/// otherwise spin forever inside a single tick and hang the server with no
	/// diagnostic at all.
	/// </summary>
	private const int MaxStepsPerTick = 10_000;

	private readonly Stack<IEnumerator> stack = new();
	private Wait? pending;
	private int ticksWaited;

	/// <summary>Creates a driver for a test body.</summary>
	/// <param name="body">The test body.</param>
	/// <param name="timeoutTicks">
	/// Tick budget, or zero for no limit. A budget is strongly advised: an
	/// unbounded gameplay test that never completes hangs the whole run.
	/// </param>
	public TestCoroutine(IEnumerator body, int timeoutTicks = 0)
	{
		ArgumentNullException.ThrowIfNull(body);

		if (timeoutTicks < 0)
			throw new ArgumentOutOfRangeException(nameof(timeoutTicks), timeoutTicks, "Timeout cannot be negative.");

		stack.Push(body);
		TimeoutTicks = timeoutTicks;
	}

	/// <summary>The tick budget, or zero for none.</summary>
	public int TimeoutTicks { get; }

	/// <summary>Ticks consumed so far.</summary>
	public int ElapsedTicks { get; private set; }

	/// <summary>Where the body has got to.</summary>
	public CoroutineState State { get; private set; } = CoroutineState.Running;

	/// <summary>
	/// What the body threw, if anything. The runner classifies an
	/// <see cref="AssertionException"/> as a failure and anything else as an
	/// error, since the first means the subject is broken and the second
	/// usually means the test is.
	/// </summary>
	public Exception? Exception { get; private set; }

	/// <summary>True once the body can make no further progress.</summary>
	public bool IsTerminal => State != CoroutineState.Running;

	/// <summary>
	/// Advances by one game tick.
	/// </summary>
	/// <returns>True while the body is still running.</returns>
	public bool Step()
	{
		if (IsTerminal)
			return false;

		ElapsedTicks++;

		if (TimeoutTicks > 0 && ElapsedTicks > TimeoutTicks) {
			State = CoroutineState.TimedOut;
			Exception = new TimeoutException(
				$"Test exceeded its budget of {TimeoutTicks} ticks while blocked on {pending?.ToString() ?? "its body"}.");
			return false;
		}

		if (pending is not null) {
			ticksWaited++;

			if (!pending.IsSatisfied(ticksWaited))
				return true;

			pending = null;
			ticksWaited = 0;
		}

		return Advance();
	}

	/// <summary>
	/// Runs to completion, at most <paramref name="maxTicks"/> ticks, calling
	/// <paramref name="onTick"/> after each. Convenience for tests of the
	/// driver itself and for Tier 0 use; the real runner steps the tick loop.
	/// </summary>
	public CoroutineState RunToCompletion(int maxTicks = 100_000, Action? onTick = null)
	{
		for (int i = 0; i < maxTicks && !IsTerminal; i++) {
			Step();
			onTick?.Invoke();
		}

		return State;
	}

	private bool Advance()
	{
		for (int guard = 0; guard < MaxStepsPerTick; guard++) {
			IEnumerator top = stack.Peek();
			bool moved;

			try {
				moved = top.MoveNext();
			}
			catch (Exception ex) {
				Exception = ex;
				State = CoroutineState.Faulted;
				return false;
			}

			if (!moved) {
				stack.Pop();

				if (stack.Count == 0) {
					State = CoroutineState.Completed;
					return false;
				}

				continue;
			}

			switch (top.Current) {
				case IEnumerator nested:
					stack.Push(nested);
					continue;

				case Wait wait:
					// A wait that is already satisfied costs no tick, so a
					// Wait.Until on an already-true condition resumes at once.
					if (wait.IsSatisfied(0))
						continue;

					pending = wait;
					ticksWaited = 0;
					return true;

				case null:
					pending = Wait.NextTick;
					ticksWaited = 0;
					return true;

				default:
					Exception = new InvalidOperationException(
						$"A test body yielded {top.Current.GetType().Name}, which means nothing to the scheduler. " +
						"Yield null for the next tick, a Wait, or a nested IEnumerator.");
					State = CoroutineState.Faulted;
					return false;
			}
		}

		Exception = new InvalidOperationException(
			$"A test body made {MaxStepsPerTick} steps within a single tick without yielding a wait that takes time. " +
			"A loop yielding only Wait.Ticks(0) or an always-true Wait.Until will do this.");
		State = CoroutineState.Faulted;

		return false;
	}
}
