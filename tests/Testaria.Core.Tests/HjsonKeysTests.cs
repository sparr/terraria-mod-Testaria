namespace Testaria.Tests;

/// <summary>
/// <see cref="HjsonKeys"/>, which decides whether a translated localization
/// file declares anything the English one does not.
/// <para/>
/// The BOM case has a test of its own because it is the one that went wrong
/// first, and it went wrong quietly: a mark on the first line stopped that line
/// being read as an opening brace, so every key beneath it was recorded at the
/// wrong depth and four keys were reported unreachable in a mod where none was.
/// </summary>
public class HjsonKeysTests
{
	/// <summary>hjson's multi-line value marker, kept out of the string literals that use it.</summary>
	private const string Fence = "'''";

	private const string English = """
		Sandstorm: {
			Keyword: Sandstorm
			Description: Summonable via a scarab
		}
		""";

	[Fact]
	public void Nested_keys_are_reported_by_their_full_path()
	{
		IReadOnlySet<string> keys = HjsonKeys.Paths(English);

		XAssert.Contains("Sandstorm.Keyword", keys);
		XAssert.Contains("Sandstorm.Description", keys);
		XAssert.Equal(2, keys.Count);
	}

	/// <summary>
	/// A byte order mark on the first line must not change what the file
	/// declares. This is the regression the corpus taught.
	/// </summary>
	[Fact]
	public void A_byte_order_mark_does_not_change_the_paths()
	{
		XAssert.Equal(
			HjsonKeys.Paths(English).OrderBy(key => key),
			HjsonKeys.Paths('﻿' + English).OrderBy(key => key));
	}

	[Fact]
	public void A_commented_entry_declares_nothing()
	{
		IReadOnlySet<string> keys = HjsonKeys.Paths("""
			Item: {
				DisplayName: Thing
				// Tooltip: ""
				# Another: no
			}
			""");

		XAssert.Contains("Item.DisplayName", keys);
		XAssert.DoesNotContain("Item.Tooltip", keys);
		XAssert.DoesNotContain("Item.Another", keys);
	}

	/// <summary>An empty value is a filled-in key, not a missing one.</summary>
	[Fact]
	public void An_empty_value_still_declares_its_key()
		=> XAssert.Contains("Item.Tooltip", HjsonKeys.Paths("Item: {\n\tTooltip: \"\"\n}"));

	[Fact]
	public void A_multiline_block_hides_its_contents()
	{
		IReadOnlySet<string> keys = HjsonKeys.Paths("""
			Item: {
				Lore:
					'''
					Nested: looking text
					Another: one
					'''
				DisplayName: Thing
			}
			""");

		XAssert.Contains("Item.DisplayName", keys);
		XAssert.DoesNotContain("Item.Nested", keys);
		XAssert.DoesNotContain("Item.Another", keys);
	}

	/// <summary>
	/// A value fenced on one line still declares its key. This is the second
	/// regression the corpus taught: skipping every line containing a fence
	/// lost such keys from the English baseline, and the same keys then looked
	/// unreachable in two translations that had them correctly.
	/// </summary>
	[Fact]
	public void A_value_fenced_on_one_line_still_declares_its_key()
	{
		IReadOnlySet<string> keys = HjsonKeys.Paths(
			"Config: {\n\tTips: " + Fence + "enabled" + Fence + "\n\tLabel: Thing\n}");

		XAssert.Contains("Config.Tips", keys);
		XAssert.Contains("Config.Label", keys);
	}

	/// <summary>
	/// And the shape it must not be confused with: a fence that opens a block
	/// for the lines beneath it declares its key and hides what follows.
	/// </summary>
	[Fact]
	public void A_fence_opening_a_block_declares_its_key_and_hides_the_rest()
	{
		IReadOnlySet<string> keys = HjsonKeys.Paths(
			"Config: {\n\tTips: " + Fence + "\n\tHidden: text\n\t" + Fence + "\n\tLabel: Thing\n}");

		XAssert.Contains("Config.Tips", keys);
		XAssert.Contains("Config.Label", keys);
		XAssert.DoesNotContain("Config.Hidden", keys);
	}

	[Fact]
	public void A_translation_declaring_only_known_keys_has_nothing_unreachable()
		=> XAssert.Empty(HjsonKeys.Unreachable(English, """
			Sandstorm: {
				Keyword: Песчаная буря
				Description: Призывается с помощью скарабея
			}
			"""));

	/// <summary>
	/// The defect: a key only the translation has. tModLoader registers keys
	/// from English alone, so this one can never be reached.
	/// </summary>
	[Fact]
	public void A_key_only_the_translation_has_is_unreachable()
	{
		IReadOnlySet<string> dead = HjsonKeys.Unreachable(English, """
			Sandstorm: {
				Keyword: Sandsturm
				Description: Beschreibung
				Extra: nobody will ever see this
			}
			""");

		XAssert.Equal(["Sandstorm.Extra"], dead);
	}

	/// <summary>
	/// A key English has and the translation lacks is untranslated, which
	/// renders in English and is correct. Only the other direction is a defect.
	/// </summary>
	[Fact]
	public void A_key_the_translation_omits_is_not_a_finding()
		=> XAssert.Empty(HjsonKeys.Unreachable(English, "Sandstorm: {\n\tKeyword: Sandsturm\n}"));

	[Fact]
	public void An_empty_file_declares_nothing()
	{
		XAssert.Empty(HjsonKeys.Paths(""));
		XAssert.Empty(HjsonKeys.Paths("\n\n  \n"));
	}
}
