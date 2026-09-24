using System.Collections;

namespace Testaria.Tests;

public class PacingDiscoveryTests
{
	private static TestCase Find<T>(string name)
		=> TestDiscovery.Discover([typeof(T)]).Tests.Single(t => t.Name == name);

	[Fact]
	public void An_unmarked_test_is_neither_realtime_nor_paused()
	{
		TestCase test = Find<Marked>(nameof(Marked.Ordinary));

		XAssert.False(test.RealTime);
		XAssert.False(test.StartPaused);
	}

	[Fact]
	public void RealTime_on_a_method_opts_that_test_out_of_fast_forward()
		=> XAssert.True(Find<Marked>(nameof(Marked.OptedOut)).RealTime);

	[Fact]
	public void StartPaused_on_a_method_is_recorded()
		=> XAssert.True(Find<Marked>(nameof(Marked.Frozen)).StartPaused);

	[Fact]
	public void Both_markers_can_apply_to_one_test()
	{
		TestCase test = Find<Marked>(nameof(Marked.FrozenAndRealTime));

		XAssert.True(test.RealTime);
		XAssert.True(test.StartPaused);
	}

	[Fact]
	public void RealTime_on_a_class_covers_every_test_in_it()
	{
		DiscoveryResult result = TestDiscovery.Discover([typeof(WholeClassRealTime)]);

		XAssert.NotEmpty(result.Tests);
		XAssert.All(result.Tests, t => XAssert.True(t.RealTime));
	}

	[Fact]
	public void StartPaused_on_a_class_covers_every_test_in_it()
	{
		DiscoveryResult result = TestDiscovery.Discover([typeof(WholeClassPaused)]);

		XAssert.NotEmpty(result.Tests);
		XAssert.All(result.Tests, t => XAssert.True(t.StartPaused));
	}

	[Fact]
	public void Every_case_of_a_parameterised_test_inherits_the_markers()
	{
		IReadOnlyList<TestCase> cases = TestDiscovery.Discover([typeof(Marked)]).Tests
			.Where(t => t.Method.Name == nameof(Marked.Parameterised)).ToList();

		XAssert.Equal(2, cases.Count);
		XAssert.All(cases, c => XAssert.True(c.RealTime));
	}

	private class Marked
	{
		[LoadedTest]
		public void Ordinary() { }

		[LoadedTest]
		[RealTime]
		public void OptedOut() { }

		[GameTest]
		[StartPaused]
		public IEnumerator Frozen() { yield break; }

		[GameTest]
		[StartPaused]
		[RealTime]
		public IEnumerator FrozenAndRealTime() { yield break; }

		[LoadedTest]
		[RealTime]
		[Case(1)]
		[Case(2)]
		public void Parameterised(int value) { }
	}

	[RealTime]
	private class WholeClassRealTime
	{
		[LoadedTest]
		public void One() { }

		[LoadedTest]
		public void Two() { }
	}

	[StartPaused]
	private class WholeClassPaused
	{
		[GameTest]
		public IEnumerator One() { yield break; }
	}
}
