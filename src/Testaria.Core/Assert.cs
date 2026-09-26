using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

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
	[ThreadStatic]
	private static int invocations;

	/// <summary>
	/// How many assertions have been made on this thread.
	/// <para/>
	/// Only the difference across a test is meaningful, and only the runner
	/// looks at it: a test that finishes without a single assertion reaching
	/// it has passed without checking anything, and says so on its result. It
	/// is not an error, because "this does not throw" is a real thing to test
	/// and needs no assertion to express, but it is worth saying out loud.
	/// This framework's own suites produced two such tests in one afternoon,
	/// both of them looping over a collection that was always empty.
	/// <para/>
	/// Per thread rather than per process, which is both simpler and more
	/// truthful. In the game everything happens on the update thread: the
	/// runner drives bodies from <c>PostUpdateEverything</c>, so a test's
	/// assertions and the runner's reading of this number are the same thread
	/// and the count is exact. A process-wide counter would also be wrong
	/// wherever suites run in parallel, and this framework's own tier 0 suite
	/// is such a place: a global count made the note tests flaky, because
	/// another test asserting on another thread looked, from here, like the
	/// test under examination having asserted.
	/// </summary>
	public static int Invocations => invocations;

	/// <summary>Records that an assertion was reached, whatever it concludes.</summary>
	private static void Counted() => invocations++;

	/// <summary>Fails unconditionally.</summary>
	[DoesNotReturn]
	public static void Fail(string message)
	{
		Counted();

		throw new AssertionException(message);
	}

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
		Counted();

		if (!condition)
			throw new AssertionException(message ?? "Assert.True() Failure\nExpected: True\nActual:   False");
	}

	/// <summary>Asserts that a condition does not hold.</summary>
	public static void False([DoesNotReturnIf(true)] bool condition, string? message = null)
	{
		Counted();

		if (condition)
			throw new AssertionException(message ?? "Assert.False() Failure\nExpected: False\nActual:   True");
	}

	/// <summary>
	/// Asserts that two values are equal.
	/// <para/>
	/// Collections are compared element by element, the way xUnit's
	/// <c>Assert.Equal</c> does. The default equality of a <c>List&lt;T&gt;</c>
	/// is reference equality, so without this a test comparing two lists of
	/// the same strings fails, and fails with "Expected:
	/// System.Collections.Generic.List`1[System.String], Actual:
	/// System.Collections.Generic.List`1[System.String]", which says nothing
	/// at all. That happened while writing this framework's own tests, twice.
	/// <para/>
	/// Strings are not treated as collections of characters, for the obvious
	/// reason.
	/// </summary>
	public static void Equal<T>(T expected, T actual, string? message = null)
	{
		Counted();

		if (AreEqual(expected, actual))
			return;

		throw new AssertionException(message ?? Describe("Assert.Equal()", expected, actual));
	}

	/// <summary>Asserts that two values are not equal.</summary>
	public static void NotEqual<T>(T notExpected, T actual, string? message = null)
	{
		Counted();

		if (!AreEqual(notExpected, actual))
			return;

		throw new AssertionException(message ?? $"Assert.NotEqual() Failure\nExpected: not {Format(notExpected)}\nActual:   {Format(actual)}");
	}

	/// <summary>
	/// Structural equality: element by element for sequences, the type's own
	/// equality for everything else.
	/// </summary>
	private static bool AreEqual<T>(T expected, T actual)
	{
		// The type's own equality first, so anything that has defined what it
		// means to be equal keeps saying so, arrays and lists included when
		// they happen to be the same object.
		if (EqualityComparer<T>.Default.Equals(expected, actual))
			return true;

		// A string is IEnumerable<char>, and comparing two unequal strings
		// character by character would report a difference at an index rather
		// than showing the strings.
		if (expected is string || actual is string)
			return false;

		if (expected is not IEnumerable left || actual is not IEnumerable right)
			return false;

		IEnumerator one = left.GetEnumerator();
		IEnumerator two = right.GetEnumerator();

		try {
			while (true) {
				bool hasOne = one.MoveNext();
				bool hasTwo = two.MoveNext();

				if (hasOne != hasTwo)
					return false;

				if (!hasOne)
					return true;

				if (!Equals(one.Current, two.Current))
					return false;
			}
		}
		finally {
			(one as IDisposable)?.Dispose();
			(two as IDisposable)?.Dispose();
		}
	}

	/// <summary>Asserts that a reference is null.</summary>
	public static void Null(object? value, string? message = null)
	{
		Counted();

		if (value is not null)
			throw new AssertionException(message ?? Describe("Assert.Null()", null, value));
	}

	/// <summary>Asserts that a reference is not null.</summary>
	public static void NotNull([NotNull] object? value, string? message = null)
	{
		Counted();

		if (value is null)
			throw new AssertionException(message ?? "Assert.NotNull() Failure\nExpected: not null\nActual:   null");
	}

	/// <summary>Asserts that two references are the same instance.</summary>
	public static void Same(object? expected, object? actual, string? message = null)
	{
		Counted();

		if (!ReferenceEquals(expected, actual))
			throw new AssertionException(message ?? Describe("Assert.Same()", expected, actual));
	}

	/// <summary>Asserts that two references are not the same instance.</summary>
	public static void NotSame(object? notExpected, object? actual, string? message = null)
	{
		Counted();

		if (ReferenceEquals(notExpected, actual))
			throw new AssertionException(message ?? $"Assert.NotSame() Failure\nExpected: not {Format(notExpected)}\nActual:   {Format(actual)}");
	}

	/// <summary>Asserts that a value lies within an inclusive range.</summary>
	public static void InRange<T>(T actual, T low, T high, string? message = null) where T : IComparable<T>
	{
		Counted();

		if (actual.CompareTo(low) < 0 || actual.CompareTo(high) > 0)
			throw new AssertionException(message ?? $"Assert.InRange() Failure\nRange:  [{Format(low)}, {Format(high)}]\nActual: {Format(actual)}");
	}

	/// <summary>Asserts that a sequence contains a value.</summary>
	public static void Contains<T>(T expected, IEnumerable<T> collection, string? message = null)
	{
		Counted();

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
		Counted();

		if (collection is null)
			return;

		foreach (T item in collection) {
			if (EqualityComparer<T>.Default.Equals(notExpected, item))
				throw new AssertionException(message ?? $"Assert.DoesNotContain() Failure\nFound: {Format(notExpected)}");
		}
	}

	/// <summary>
	/// Asserts that a string contains a substring.
	/// <para/>
	/// A string is a sequence of characters, so without this overload
	/// <c>Assert.Contains("left the box", note)</c> would not compile at all,
	/// and the nearest thing that did compile would test one character. The
	/// exact-match overload wins resolution over the generic one, which is
	/// what makes the obvious call mean the obvious thing.
	/// </summary>
	public static void Contains(string expected, string? actual, string? message = null)
	{
		Counted();

		if (actual is null || !actual.Contains(expected, StringComparison.Ordinal))
			throw new AssertionException(message ?? $"Assert.Contains() Failure\nNot found: {Format(expected)}\nIn string: {Format(actual)}");
	}

	/// <summary>Asserts that a string does not contain a substring.</summary>
	public static void DoesNotContain(string notExpected, string? actual, string? message = null)
	{
		Counted();

		if (actual is not null && actual.Contains(notExpected, StringComparison.Ordinal))
			throw new AssertionException(message ?? $"Assert.DoesNotContain() Failure\nFound:     {Format(notExpected)}\nIn string: {Format(actual)}");
	}

	/// <summary>Asserts that a sequence has no elements.</summary>
	public static void Empty(IEnumerable collection, string? message = null)
	{
		Counted();

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
		Counted();

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
		Counted();

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
		// Contents, not the type name. A collection whose ToString is
		// "System.Collections.Generic.List`1[System.String]" tells the reader
		// nothing about why their assertion failed, and printing that on both
		// the expected and actual lines is worse than printing nothing.
		IEnumerable sequence => FormatSequence(sequence),
		_ => value.ToString() ?? "null",
	};

	/// <summary>
	/// Asserts that no property throws when set to the value its own getter
	/// just produced.
	/// <para/>
	/// Not a check that the value survives: a property may normalize, clamp or
	/// reformat what it is given, and all of that is fine here. The only
	/// question asked is whether the assignment throws, because reading a
	/// property and writing it straight back is what a text field does every
	/// time somebody opens it and closes it again, and a property that cannot
	/// take its own output turns that into a crash.
	/// <para/>
	/// Give it a **newly constructed** object. The defect this exists to find
	/// lives in the empty, null and zero cases, which is where a getter
	/// produces something its parser was never given: the property that
	/// prompted this renders an unset value as the empty string and parses
	/// with a method that throws on one, so it failed on a fresh object and
	/// would have passed on a populated one.
	/// <para/>
	/// Public instance properties that are both readable and writable, not
	/// indexers. Every such property is tried before anything is reported, so
	/// one bad property does not hide the rest.
	/// </summary>
	public static void SettersAcceptTheirOwnGetters(object subject, string? message = null)
	{
		Counted();

		NotNull(subject, "Assert.SettersAcceptTheirOwnGetters() Failure\nSubject was null");

		List<string> problems = [];

		foreach (PropertyInfo property in RoundTrippable(subject.GetType())) {
			object? value;
			try {
				value = property.GetValue(subject);
			}
			catch (Exception bad) {
				problems.Add($"{property.Name}: reading it threw {Unwrap(bad)}");
				continue;
			}

			try {
				property.SetValue(subject, value);
			}
			catch (Exception bad) {
				problems.Add($"{property.Name}: reading it gave {Format(value)}, "
					+ $"and writing that back threw {Unwrap(bad)}");
			}
		}

		if (problems.Count == 0)
			return;

		string count = problems.Count == 1
			? "1 property could not take its own value"
			: $"{problems.Count} properties could not take their own values";

		throw new AssertionException(message ?? "Assert.SettersAcceptTheirOwnGetters() Failure\n"
			+ $"On a {subject.GetType().Name}, {count}:\n  "
			+ string.Join("\n  ", problems));
	}

	/// <summary>
	/// Asserts that writing a property back a second time changes nothing.
	/// <para/>
	/// Read, write it back, read again, write that back, and read once more:
	/// the last two reads must agree. The first write is allowed to change the
	/// value, because a property may well normalize what it is given, and
	/// <c>"2:5"</c> coming back as <c>"2:05"</c> is a property doing its job.
	/// What it may not do is keep changing: a second write that moves the value
	/// again means the property has no resting state, and every pass through
	/// the interface it belongs to drifts a little further.
	/// <para/>
	/// This is the sibling of <see cref="SettersAcceptTheirOwnGetters"/> and
	/// catches a different fault. That one is about a crash; this one is about
	/// a value that never settles, which appends, re-escapes, re-encodes or
	/// truncates a bit more each time and throws nothing at all while doing it.
	/// <para/>
	/// A property that throws is reported rather than passed, because a
	/// property that cannot be written cannot be shown to settle.
	/// </summary>
	public static void SettersSettleAfterOneWrite(object subject, string? message = null)
	{
		Counted();

		NotNull(subject, "Assert.SettersSettleAfterOneWrite() Failure\nSubject was null");

		List<string> problems = [];

		foreach (PropertyInfo property in RoundTrippable(subject.GetType())) {
			object? first, second, third;

			try {
				first = property.GetValue(subject);
				property.SetValue(subject, first);
				second = property.GetValue(subject);
				property.SetValue(subject, second);
				third = property.GetValue(subject);
			}
			catch (Exception bad) {
				problems.Add($"{property.Name}: could not be written back at all, which threw {Unwrap(bad)}");
				continue;
			}

			if (AreEqual<object?>(second, third))
				continue;

			problems.Add($"{property.Name}: started as {Format(first)}, became {Format(second)} "
				+ $"after one write, and {Format(third)} after a second, so it never settles");
		}

		if (problems.Count == 0)
			return;

		string count = problems.Count == 1
			? "1 property does not settle"
			: $"{problems.Count} properties do not settle";

		throw new AssertionException(message ?? "Assert.SettersSettleAfterOneWrite() Failure\n"
			+ $"On a {subject.GetType().Name}, {count}:\n  "
			+ string.Join("\n  ", problems));
	}

	/// <summary>
	/// The properties a value can be read from and written straight back to.
	/// <para/>
	/// Public instance properties with both accessors public, excluding
	/// indexers, which are not a value a field can hold and have no argument to
	/// be given.
	/// </summary>
	private static IEnumerable<PropertyInfo> RoundTrippable(Type type)
	{
		foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
			if (property.GetMethod is not { IsPublic: true } || property.SetMethod is not { IsPublic: true })
				continue;

			if (property.GetIndexParameters().Length != 0)
				continue;

			yield return property;
		}
	}

	/// <summary>
	/// The exception a reflected call actually threw, described.
	/// <para/>
	/// Reflection wraps whatever the accessor threw in a
	/// <see cref="TargetInvocationException"/>, which names nothing useful.
	/// </summary>
	private static string Unwrap(Exception thrown)
	{
		Exception real = thrown is TargetInvocationException { InnerException: { } inner } ? inner : thrown;

		return $"{real.GetType().Name}: {real.Message}";
	}

	/// <summary>
	/// A sequence as its elements, truncated so that a large collection does
	/// not bury the assertion that mentions it.
	/// </summary>
	private static string FormatSequence(IEnumerable sequence)
	{
		const int Limit = 10;

		List<string> parts = [];
		int count = 0;

		foreach (object? element in sequence) {
			count++;

			if (parts.Count < Limit)
				parts.Add(Format(element));
		}

		string body = string.Join(", ", parts);

		return count > Limit
			? $"[{body}, ... {count} in total]"
			: $"[{body}]";
	}
}
