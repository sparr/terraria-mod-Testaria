namespace Testaria.Tests;

/// <summary>
/// That Assert.Equal compares collections by their contents.
/// <para/>
/// The default equality of a List is reference equality, so without this a test
/// comparing two lists of the same strings fails with "Expected:
/// System.Collections.Generic.List`1[System.String], Actual:
/// System.Collections.Generic.List`1[System.String]" on both lines. Since the
/// vocabulary here deliberately mirrors xUnit's, and xUnit's Assert.Equal does
/// compare sequences, anyone arriving from xUnit walks straight into it.
/// </summary>
public class AssertSequenceTests
{
	[Fact]
	public void Two_lists_with_the_same_contents_are_equal()
		=> Assert.Equal(new List<string> { "a", "b" }, new List<string> { "a", "b" });

	[Fact]
	public void Two_arrays_with_the_same_contents_are_equal()
		=> Assert.Equal(new[] { 1, 2, 3 }, new[] { 1, 2, 3 });

	[Fact]
	public void A_list_and_an_array_of_the_same_contents_are_equal()
	{
		// Typed as IEnumerable<int> so both sides share a static type, which
		// is how this is usually written.
		IEnumerable<int> list = new List<int> { 1, 2 };
		IEnumerable<int> array = new[] { 1, 2 };

		Assert.Equal(list, array);
	}

	[Fact]
	public void Different_contents_are_not_equal()
		=> XAssert.Throws<AssertionException>(
			() => Assert.Equal(new List<string> { "a" }, new List<string> { "b" }));

	[Fact]
	public void A_longer_sequence_is_not_equal_to_a_shorter_one()
	{
		XAssert.Throws<AssertionException>(
			() => Assert.Equal(new[] { 1, 2 }, new[] { 1 }));
		XAssert.Throws<AssertionException>(
			() => Assert.Equal(new[] { 1 }, new[] { 1, 2 }));
	}

	[Fact]
	public void Order_matters()
		=> XAssert.Throws<AssertionException>(
			() => Assert.Equal(new[] { 1, 2 }, new[] { 2, 1 }));

	[Fact]
	public void Empty_sequences_are_equal()
		=> Assert.Equal(new List<int>(), Array.Empty<int>().ToList());

	[Fact]
	public void Strings_are_still_compared_as_strings()
	{
		// A string is IEnumerable<char>. Comparing two unequal strings element
		// by element would report a difference at an index instead of showing
		// the strings, which is a worse message, not a better one.
		Assert.Equal("ab", "ab");

		AssertionException thrown = XAssert.Throws<AssertionException>(() => Assert.Equal("ab", "ac"));

		XAssert.Contains("\"ab\"", thrown.Message);
		XAssert.Contains("\"ac\"", thrown.Message);
	}

	[Fact]
	public void The_message_shows_the_elements_rather_than_the_type_name()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => Assert.Equal(new List<string> { "a" }, new List<string> { "b" }));

		XAssert.Contains("[\"a\"]", thrown.Message);
		XAssert.Contains("[\"b\"]", thrown.Message);
		XAssert.DoesNotContain("System.Collections", thrown.Message);
	}

	[Fact]
	public void A_long_sequence_is_truncated_rather_than_dumped()
	{
		AssertionException thrown = XAssert.Throws<AssertionException>(
			() => Assert.Equal(Enumerable.Range(0, 50).ToList(), new List<int>()));

		XAssert.Contains("50 in total", thrown.Message);
		// Truncated, so the assertion that mentions it is still readable.
		XAssert.DoesNotContain("49", thrown.Message);
	}

	[Fact]
	public void NotEqual_agrees_with_Equal_about_sequences()
	{
		Assert.NotEqual(new[] { 1, 2 }, new[] { 2, 1 });

		XAssert.Throws<AssertionException>(
			() => Assert.NotEqual(new List<string> { "a" }, new List<string> { "a" }));
	}

	[Fact]
	public void Nulls_are_still_handled()
	{
		Assert.Equal<List<int>?>(null, null);

		XAssert.Throws<AssertionException>(() => Assert.Equal(null, new List<int>()));
		XAssert.Throws<AssertionException>(() => Assert.Equal(new List<int>(), null));
	}

	[Fact]
	public void Scalars_are_unaffected()
	{
		Assert.Equal(3, 3);

		XAssert.Throws<AssertionException>(() => Assert.Equal(3, 4));
	}
}
