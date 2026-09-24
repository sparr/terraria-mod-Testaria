namespace Testaria.Tests;

/// <summary>
/// Seed derivation, which is worth more tests than its size suggests.
/// <para/>
/// The whole value of seeding is that a reported seed reproduces a reported
/// failure. That property is destroyed by any of: a seed that varies between
/// processes, a seed that depends on how many tests ran first, or a seed that
/// changes when the framework is updated. Each of those has a test here.
/// </summary>
public class TestSeedTests
{
	private const string Class = "MyMod.Tests.ZombieTests";
	private const string Name = "A_zombie_targets_the_player";

	[Fact]
	public void The_same_test_gets_the_same_seed_every_time()
		=> XAssert.Equal(TestSeed.For(0, Class, Name), TestSeed.For(0, Class, Name));

	// Pinned values, computed independently rather than copied from the
	// implementation's own output. They are a compatibility surface on
	// purpose: a change to the algorithm makes every previously reported seed
	// meaningless, so it should have to be done deliberately, with this test
	// in hand, rather than as a side effect of tidying the hash.
	[Theory]
	[InlineData(0, Name, 1124590588)]
	[InlineData(7, Name, 1124589737)]
	[InlineData(0, Name + "(1)", 282574919)]
	[InlineData(-1, Name, 76063757)]
	public void Seeds_are_the_values_they_have_always_been(int runSeed, string name, int expected)
		=> XAssert.Equal(expected, TestSeed.For(runSeed, Class, name));

	[Fact]
	public void Two_tests_in_the_same_class_get_different_seeds()
		=> XAssert.NotEqual(TestSeed.For(0, Class, Name), TestSeed.For(0, Class, Name + "_twice"));

	[Fact]
	public void The_same_name_in_two_classes_gets_different_seeds()
		=> XAssert.NotEqual(TestSeed.For(0, Class, Name), TestSeed.For(0, "MyMod.Tests.SlimeTests", Name));

	[Fact]
	public void Two_cases_of_one_test_get_different_seeds()
		// The case arguments are part of the name, so this follows, but it is
		// the property that matters: two cases rolling identically would hide
		// a bug that only shows up on one roll.
		=> XAssert.NotEqual(TestSeed.For(0, Class, Name + "(1)"), TestSeed.For(0, Class, Name + "(2)"));

	[Fact]
	public void The_run_seed_shifts_every_test()
		=> XAssert.NotEqual(TestSeed.For(0, Class, Name), TestSeed.For(1, Class, Name));

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(int.MinValue)]
	[InlineData(int.MaxValue)]
	public void A_seed_is_never_negative(int runSeed)
		// A negative seed is legal for UnifiedRandom, which takes its absolute
		// value, but reads like a bug in a report.
		=> XAssert.InRange(TestSeed.For(runSeed, Class, Name), 0, int.MaxValue);

	[Fact]
	public void An_empty_name_is_still_a_seed()
		=> XAssert.InRange(TestSeed.For(0, string.Empty, string.Empty), 0, int.MaxValue);
}
