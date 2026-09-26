using Testaria;
using static TestingEfficiency.DataStructures;

namespace TestingEfficiencyTests;

/// <summary>
/// The two string properties a boss result is edited through.
/// <para/>
/// <c>timeString</c> and <c>diedString</c> are what the interface reads a
/// stored number into and writes an edited one back out of, so they are a
/// round trip whether or not they were written as one. Pure computation, and
/// tier 1 only because the type lives in a mod assembly that has to be loaded
/// before anything can name it.
/// </summary>
public class BossTestDataTests
{
	/// <summary>A duration in seconds reads back as minutes and seconds.</summary>
	[LoadedTest]
	[Case(0, "")]
	[Case(5, "0:05")]
	[Case(59, "0:59")]
	[Case(60, "1:00")]
	[Case(61, "1:01")]
	[Case(150, "2:30")]
	[Case(600, "10:00")]
	[Case(3599, "59:59")]
	public void Time_reads_back_as_minutes_and_seconds(int time, string expected)
		=> Assert.Equal(expected, new BossTestData { time = time }.timeString);

	/// <summary>And the same text sets the same number again.</summary>
	[LoadedTest]
	[Case("0:05", 5)]
	[Case("0:59", 59)]
	[Case("1:00", 60)]
	[Case("2:30", 150)]
	[Case("10:00", 600)]
	public void Time_parses_back_to_the_same_seconds(string text, int expected)
		=> Assert.Equal(expected, new BossTestData { timeString = text }.time);

	/// <summary>
	/// A bare number is taken as seconds, not as minutes.
	/// <para/>
	/// Worth pinning: it is the one input where the two halves could disagree
	/// without looking wrong, and a player typing 90 means ninety seconds.
	/// </summary>
	[LoadedTest]
	public void A_bare_number_is_seconds()
		=> Assert.Equal(90, new BossTestData { timeString = "90" }.time);

	/// <summary>
	/// Whatever the getter produces, the setter accepts.
	/// <para/>
	/// This is the property that matters for a value shown in a text box and
	/// written back unedited, which is what happens every time a person opens
	/// a field and closes it again.
	/// </summary>
	[LoadedTest]
	[Case(0)]
	[Case(5)]
	[Case(61)]
	[Case(150)]
	public void Time_survives_a_round_trip_through_its_own_text(int time)
	{
		var data = new BossTestData { time = time };
		data.timeString = data.timeString;

		Assert.Equal(time, data.time, "reading the text and writing it back changed the value");
	}

	/// <summary>Text that is not a duration leaves the value alone rather than throwing.</summary>
	[LoadedTest]
	[Case("")]
	[Case("nonsense")]
	[Case(":")]
	public void Unparseable_time_is_ignored(string text)
		=> Assert.Equal(42, new BossTestData { time = 42, timeString = text }.time);

	/// <summary>A survival fraction reads back as a percentage.</summary>
	[LoadedTest]
	[Case(1f, "100%")]
	[Case(0.5f, "50%")]
	[Case(0.125f, "12.5%")]
	public void Died_reads_back_as_a_percentage(float died, string expected)
		=> Assert.Equal(expected, new BossTestData { died = died }.diedString);

	/// <summary>And the same text sets the same fraction again.</summary>
	[LoadedTest]
	[Case("100%", 1f)]
	[Case("50%", 0.5f)]
	[Case("12.5%", 0.125f)]
	public void Died_parses_back_to_the_same_fraction(string text, float expected)
		=> Assert.Equal(expected, new BossTestData { diedString = text }.died);

	/// <summary>
	/// The same round trip <c>timeString</c> survives, for the sibling property.
	/// <para/>
	/// Separate from the paired cases above because the interesting value is
	/// the one with no percentage at all, which the getter renders as empty
	/// and which therefore has to go back in as empty.
	/// </summary>
	[LoadedTest]
	public void An_unset_died_survives_a_round_trip_through_its_own_text()
	{
		var data = new BossTestData();
		Assert.Equal(string.Empty, data.diedString, "an unset result should render as nothing");

		data.diedString = data.diedString;

		Assert.Equal(null, data.died, "reading the text and writing it back changed the value");
	}

	/// <summary>
	/// A result recorded at zero reads back as unset.
	/// <para/>
	/// Not a complaint, a boundary: <c>died</c> is backed by a property that
	/// maps zero to null, so "the boss was at zero percent" and "no result
	/// was recorded" are the same stored value and cannot be told apart.
	/// </summary>
	[LoadedTest]
	public void A_zero_result_is_indistinguishable_from_no_result()
		=> Assert.Equal(null, new BossTestData { died = 0f }.died);

	/// <summary>A fresh result is valid, and carries no text.</summary>
	[LoadedTest]
	public void A_fresh_result_is_valid_and_empty()
	{
		var data = new BossTestData();

		Assert.True(data.validTest, "a result should start valid");
		Assert.Equal(string.Empty, data.name);
		Assert.Equal(0, data.time);
	}
}
