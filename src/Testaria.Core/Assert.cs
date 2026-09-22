using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Testaria;

/// <summary>
/// Assertions for Testaria tests, at every tier.
/// <para/>
/// The vocabulary deliberately mirrors xUnit's, because the goal is that a
/// modder who has written .NET tests before can read a Testaria test without
/// learning anything new. It is a separate implementation rather than a
/// dependency because mod assemblies load from memory into a per-mod
/// <c>AssemblyLoadContext</c>, which no stock test runner is built to host.
/// </summary>
public static class Assert
{
	/// <summary>Fails unconditionally.</summary>
	[DoesNotReturn]
	public static void Fail(string message) => throw new AssertionException(message);

	/// <summary>
	/// Abandons the test as skipped, for a reason only discoverable at run
	/// time such as an optional mod being absent.
	/// <para/>
	/// Reported as skipped, never as passed: a vacuous pass claims coverage
	/// that never happened.
	/// </summary>
	[DoesNotReturn]
	public static void Skip(string reason) => throw new SkipTestException(reason);

	/// <summary>Asserts that a condition holds.</summary>
	public static void True([DoesNotReturnIf(false)] bool condition, string? message = null)
	{
		if (!condition)
			throw new AssertionException(message ?? "Assert.True() Failure\nExpected: True\nActual:   False");
	}

	/// <summary>Asserts that a condition does not hold.</summary>
	public static void False([DoesNotReturnIf(true)] bool condition, string? message = null)
	{
		if (condition)
			throw new AssertionException(message ?? "Assert.False() Failure\nExpected: False\nActual:   True");
	}

	/// <summary>Asserts that two values are equal.</summary>
	public static void Equal<T>(T expected, T actual, string? message = null)
	{
		if (!EqualityComparer<T>.Default.Equals(expected, actual))
			throw new AssertionException(message ?? Describe("Assert.Equal()", expected, actual));
	}

	/// <summary>Asserts that two values are not equal.</summary>
	public static void NotEqual<T>(T notExpected, T actual, string? message = null)
	{
		if (EqualityComparer<T>.Default.Equals(notExpected, actual))
			throw new AssertionException(message ?? $"Assert.NotEqual() Failure\nExpected: not {Format(notExpected)}\nActual:   {Format(actual)}");
	}

	/// <summary>Asserts that a reference is null.</summary>
	public static void Null(object? value, string? message = null)
	{
		if (value is not null)
			throw new AssertionException(message ?? Describe("Assert.Null()", null, value));
	}

	/// <summary>Asserts that a reference is not null.</summary>
	public static void NotNull([NotNull] object? value, string? message = null)
	{
		if (value is null)
			throw new AssertionException(message ?? "Assert.NotNull() Failure\nExpected: not null\nActual:   null");
	}

	/// <summary>Asserts that two references are the same instance.</summary>
	public static void Same(object? expected, object? actual, string? message = null)
	{
		if (!ReferenceEquals(expected, actual))
			throw new AssertionException(message ?? Describe("Assert.Same()", expected, actual));
	}

	/// <summary>Asserts that two references are not the same instance.</summary>
	public static void NotSame(object? notExpected, object? actual, string? message = null)
	{
		if (ReferenceEquals(notExpected, actual))
			throw new AssertionException(message ?? $"Assert.NotSame() Failure\nExpected: not {Format(notExpected)}\nActual:   {Format(actual)}");
	}

	/// <summary>Asserts that a value lies within an inclusive range.</summary>
	public static void InRange<T>(T actual, T low, T high, string? message = null) where T : IComparable<T>
	{
		if (actual.CompareTo(low) < 0 || actual.CompareTo(high) > 0)
			throw new AssertionException(message ?? $"Assert.InRange() Failure\nRange:  [{Format(low)}, {Format(high)}]\nActual: {Format(actual)}");
	}

	/// <summary>Asserts that a sequence contains a value.</summary>
	public static void Contains<T>(T expected, IEnumerable<T> collection, string? message = null)
	{
		if (collection is null)
			throw new AssertionException("Assert.Contains() Failure\nCollection was null");

		foreach (T item in collection) {
			if (EqualityComparer<T>.Default.Equals(expected, item))
				return;
		}

		throw new AssertionException(message ?? $"Assert.Contains() Failure\nNot found: {Format(expected)}");
	}

	/// <summary>Asserts that a sequence does not contain a value.</summary>
	public static void DoesNotContain<T>(T notExpected, IEnumerable<T> collection, string? message = null)
	{
		if (collection is null)
			return;

		foreach (T item in collection) {
			if (EqualityComparer<T>.Default.Equals(notExpected, item))
				throw new AssertionException(message ?? $"Assert.DoesNotContain() Failure\nFound: {Format(notExpected)}");
		}
	}

	/// <summary>Asserts that a sequence has no elements.</summary>
	public static void Empty(IEnumerable collection, string? message = null)
	{
		NotNull(collection, "Assert.Empty() Failure\nCollection was null");

		IEnumerator enumerator = collection.GetEnumerator();
		try {
			if (enumerator.MoveNext())
				throw new AssertionException(message ?? "Assert.Empty() Failure\nExpected: empty\nActual:   at least one element");
		}
		finally {
			(enumerator as IDisposable)?.Dispose();
		}
	}

	/// <summary>Asserts that a sequence has at least one element.</summary>
	public static void NotEmpty(IEnumerable collection, string? message = null)
	{
		NotNull(collection, "Assert.NotEmpty() Failure\nCollection was null");

		IEnumerator enumerator = collection.GetEnumerator();
		try {
			if (!enumerator.MoveNext())
				throw new AssertionException(message ?? "Assert.NotEmpty() Failure\nExpected: at least one element\nActual:   empty");
		}
		finally {
			(enumerator as IDisposable)?.Dispose();
		}
	}

	/// <summary>
	/// Asserts that an action throws <typeparamref name="T"/>, and returns the
	/// thrown exception so the caller can assert further on it. An exception of
	/// a derived type does not satisfy this assertion, matching xUnit.
	/// </summary>
	public static T Throws<T>(Action action) where T : Exception
	{
		NotNull(action);

		try {
			action();
		}
		catch (T ex) when (ex.GetType() == typeof(T)) {
			return ex;
		}
		catch (Exception ex) {
			throw new AssertionException($"Assert.Throws() Failure\nExpected: {typeof(T).FullName}\nActual:   {ex.GetType().FullName}", ex);
		}

		throw new AssertionException($"Assert.Throws() Failure\nExpected: {typeof(T).FullName}\nActual:   no exception was thrown");
	}

	private static string Describe(string assertion, object? expected, object? actual)
		=> $"{assertion} Failure\nExpected: {Format(expected)}\nActual:   {Format(actual)}";

	private static string Format(object? value) => value switch {
		null => "null",
		string s => $"\"{s}\"",
		IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString() ?? "null",
	};
}
