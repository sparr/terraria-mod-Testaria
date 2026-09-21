using TAssert = Testaria.Assert;

namespace Testaria.Tests;

/// <summary>
/// Self-tests for Testaria's own assertion surface, written in xUnit.
/// <para/>
/// Testing an assertion library with itself would be circular, so these use
/// xUnit's Assert to check Testaria's. Both are aliased, to <c>XAssert</c>
/// and <c>TAssert</c>, because an unqualified <c>Assert</c> inside namespace
/// <c>Testaria.Tests</c> binds to <c>Testaria.Assert</c> through the
/// enclosing namespace and would silently shadow xUnit's.
/// </summary>
public class AssertTests
{
	[Fact]
	public void True_passes_when_condition_holds()
		=> TAssert.True(true);

	[Fact]
	public void True_throws_when_condition_fails()
		=> XAssert.Throws<AssertionException>(() => TAssert.True(false));

	[Fact]
	public void False_passes_when_condition_is_false()
		=> TAssert.False(false);

	[Fact]
	public void False_throws_when_condition_holds()
		=> XAssert.Throws<AssertionException>(() => TAssert.False(true));

	[Fact]
	public void Custom_message_is_preserved()
	{
		var ex = XAssert.Throws<AssertionException>(() => TAssert.True(false, "the zombie should be dead"));
		XAssert.Equal("the zombie should be dead", ex.Message);
	}

	[Fact]
	public void Equal_passes_for_equal_values()
	{
		TAssert.Equal(5, 5);
		TAssert.Equal("hello", "hello");
	}

	[Fact]
	public void Equal_reports_both_sides()
	{
		var ex = XAssert.Throws<AssertionException>(() => TAssert.Equal(5, 3));
		XAssert.Contains("Expected: 5", ex.Message);
		XAssert.Contains("Actual:   3", ex.Message);
	}

	[Fact]
	public void Equal_quotes_strings_so_whitespace_is_visible()
	{
		var ex = XAssert.Throws<AssertionException>(() => TAssert.Equal("a", "a "));
		XAssert.Contains("\"a\"", ex.Message);
		XAssert.Contains("\"a \"", ex.Message);
	}

	[Fact]
	public void Equal_handles_nulls_on_either_side()
	{
		TAssert.Equal<string?>(null, null);
		XAssert.Throws<AssertionException>(() => TAssert.Equal(null, "x"));
		XAssert.Throws<AssertionException>(() => TAssert.Equal("x", null));
	}

	[Fact]
	public void NotEqual_passes_for_differing_values()
		=> TAssert.NotEqual(5, 3);

	[Fact]
	public void NotEqual_throws_for_equal_values()
		=> XAssert.Throws<AssertionException>(() => TAssert.NotEqual(5, 5));

	[Fact]
	public void Null_and_NotNull_behave()
	{
		TAssert.Null(null);
		TAssert.NotNull("x");
		XAssert.Throws<AssertionException>(() => TAssert.Null("x"));
		XAssert.Throws<AssertionException>(() => TAssert.NotNull(null));
	}

	[Fact]
	public void Same_compares_by_reference_not_value()
	{
		var a = new StringBuilder_Stub();
		var b = new StringBuilder_Stub();

		TAssert.Same(a, a);
		TAssert.NotSame(a, b);
		XAssert.Throws<AssertionException>(() => TAssert.Same(a, b));
	}

	[Fact]
	public void InRange_is_inclusive_at_both_ends()
	{
		TAssert.InRange(5, 1, 10);
		TAssert.InRange(1, 1, 10);
		TAssert.InRange(10, 1, 10);
		XAssert.Throws<AssertionException>(() => TAssert.InRange(0, 1, 10));
		XAssert.Throws<AssertionException>(() => TAssert.InRange(11, 1, 10));
	}

	[Fact]
	public void Contains_and_DoesNotContain_behave()
	{
		int[] values = [1, 2, 3];

		TAssert.Contains(2, values);
		TAssert.DoesNotContain(4, values);
		XAssert.Throws<AssertionException>(() => TAssert.Contains(4, values));
		XAssert.Throws<AssertionException>(() => TAssert.DoesNotContain(2, values));
	}

	[Fact]
	public void Empty_and_NotEmpty_behave()
	{
		TAssert.Empty(Array.Empty<int>());
		TAssert.NotEmpty(new[] { 1 });
		XAssert.Throws<AssertionException>(() => TAssert.Empty(new[] { 1 }));
		XAssert.Throws<AssertionException>(() => TAssert.NotEmpty(Array.Empty<int>()));
	}

	[Fact]
	public void Throws_returns_the_exception_for_further_assertions()
	{
		var caught = TAssert.Throws<InvalidOperationException>(() => throw new InvalidOperationException("boom"));
		XAssert.Equal("boom", caught.Message);
	}

	[Fact]
	public void Throws_fails_when_nothing_is_thrown()
	{
		var ex = XAssert.Throws<AssertionException>(() => TAssert.Throws<InvalidOperationException>(() => { }));
		XAssert.Contains("no exception was thrown", ex.Message);
	}

	[Fact]
	public void Throws_fails_on_the_wrong_exception_type_and_keeps_the_cause()
	{
		var ex = XAssert.Throws<AssertionException>(
			() => TAssert.Throws<InvalidOperationException>(() => throw new ArgumentException("wrong")));

		XAssert.Contains("ArgumentException", ex.Message);
		XAssert.IsType<ArgumentException>(ex.InnerException);
	}

	[Fact]
	public void Throws_does_not_accept_a_derived_exception_type()
	{
		// ArgumentNullException derives from ArgumentException. Matching xUnit,
		// an exact type match is required, so this must not satisfy the assertion.
		XAssert.Throws<AssertionException>(
			() => TAssert.Throws<ArgumentException>(() => throw new ArgumentNullException("p")));
	}

	[Fact]
	public void Fail_always_throws()
	{
		var ex = XAssert.Throws<AssertionException>(() => TAssert.Fail("nope"));
		XAssert.Equal("nope", ex.Message);
	}

	private sealed class StringBuilder_Stub;
}
