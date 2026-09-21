namespace Testaria;

/// <summary>
/// What a gameplay test yields when it needs time to pass.
/// <para/>
/// Terraria is a fixed 60 Hz tick loop, so a gameplay assertion is inherently
/// "over N ticks" rather than "the return value of this call". Yielding a wait
/// is how a test body says where it is willing to be suspended.
/// </summary>
public abstract class Wait
{
	/// <summary>Resume on the next tick.</summary>
	public static Wait NextTick { get; } = new TickWait(1);

	/// <summary>Resume after <paramref name="ticks"/> ticks have elapsed.</summary>
	public static Wait Ticks(int ticks)
	{
		if (ticks < 0)
			throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Cannot wait a negative number of ticks.");

		return new TickWait(ticks);
	}

	/// <summary>Resume at 60 ticks per second. Sugar over <see cref="Ticks(int)"/>.</summary>
	public static Wait Seconds(double seconds) => Ticks((int)Math.Round(seconds * 60));

	/// <summary>
	/// Resume on the first tick where <paramref name="predicate"/> holds. The
	/// predicate is evaluated immediately, so a condition that is already true
	/// costs no tick.
	/// </summary>
	public static Wait Until(Func<bool> predicate)
	{
		ArgumentNullException.ThrowIfNull(predicate);

		return new PredicateWait(predicate, expected: true);
	}

	/// <summary>Resume on the first tick where <paramref name="predicate"/> stops holding.</summary>
	public static Wait While(Func<bool> predicate)
	{
		ArgumentNullException.ThrowIfNull(predicate);

		return new PredicateWait(predicate, expected: false);
	}

	/// <summary>
	/// Whether the body may resume, given how many ticks have passed since the
	/// wait was yielded. Called with zero on the tick the wait was yielded.
	/// </summary>
	public abstract bool IsSatisfied(int ticksWaited);

	/// <summary>How this wait reads in a timeout message.</summary>
	public abstract override string ToString();

	private sealed class TickWait(int ticks) : Wait
	{
		public override bool IsSatisfied(int ticksWaited) => ticksWaited >= ticks;

		public override string ToString() => $"Wait.Ticks({ticks})";
	}

	private sealed class PredicateWait(Func<bool> predicate, bool expected) : Wait
	{
		public override bool IsSatisfied(int ticksWaited) => predicate() == expected;

		public override string ToString() => expected ? "Wait.Until(...)" : "Wait.While(...)";
	}
}
