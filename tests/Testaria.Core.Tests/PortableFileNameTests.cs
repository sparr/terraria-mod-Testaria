namespace Testaria.Tests;

public class PortableFileNameTests
{
	[Theory]
	[InlineData("<")]
	[InlineData(">")]
	[InlineData(":")]
	[InlineData("\"")]
	[InlineData("/")]
	[InlineData("\\")]
	[InlineData("|")]
	[InlineData("?")]
	[InlineData("*")]
	public void Characters_Windows_forbids_are_replaced_even_though_Linux_allows_them(string bad)
	{
		// The whole point of not using Path.GetInvalidFileNameChars: on Linux
		// it reports only the null byte and the forward slash, so a name built
		// with it would be unopenable on Windows and would break a CI artifact
		// a Windows developer later downloads.
		string safe = PortableFileName.MakeSafe($"before{bad}after");

		XAssert.DoesNotContain(bad, safe);
		XAssert.Equal("before_after", safe);
	}

	[Fact]
	public void Control_characters_are_replaced()
		=> XAssert.Equal("a_b", PortableFileName.MakeSafe("a\u0001b"));

	[Theory]
	[InlineData("CON")]
	[InlineData("con")]
	[InlineData("NUL")]
	[InlineData("COM1")]
	[InlineData("LPT9")]
	[InlineData("aux")]
	public void Reserved_device_names_are_escaped(string reserved)
	{
		string safe = PortableFileName.MakeSafe(reserved);

		XAssert.NotEqual(reserved, safe);
		XAssert.StartsWith("_", safe);
	}

	[Fact]
	public void A_reserved_name_is_still_reserved_with_an_extension()
	{
		// CON.xml is as unusable as CON on Windows.
		XAssert.True(PortableFileName.IsReservedDeviceName("CON.xml"));
		XAssert.Equal("_CON.xml", PortableFileName.MakeSafe("CON.xml"));
	}

	[Theory]
	[InlineData("CONSOLE")]
	[InlineData("COM10")]
	[InlineData("CONTEXT")]
	[InlineData("NULLABLE")]
	public void Names_that_merely_start_like_a_device_name_are_left_alone(string name)
	{
		XAssert.False(PortableFileName.IsReservedDeviceName(name));
		XAssert.Equal(name, PortableFileName.MakeSafe(name));
	}

	[Fact]
	public void Trailing_dots_and_spaces_are_trimmed_because_Windows_strips_them_silently()
	{
		// Without this the name would not round-trip: you write "run." and
		// find "run" on disk.
		XAssert.Equal("run", PortableFileName.MakeSafe("run."));
		XAssert.Equal("run", PortableFileName.MakeSafe("run   "));
		XAssert.Equal("run", PortableFileName.MakeSafe("run. . ."));
	}

	[Fact]
	public void Leading_spaces_are_trimmed()
		=> XAssert.Equal("run", PortableFileName.MakeSafe("   run"));

	[Fact]
	public void Interior_dots_and_spaces_survive()
		=> XAssert.Equal("My Mod.Tests", PortableFileName.MakeSafe("My Mod.Tests"));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("...")]
	[InlineData("   ")]
	public void Names_that_sanitize_to_nothing_fall_back(string? name)
		=> XAssert.Equal(PortableFileName.Fallback, PortableFileName.MakeSafe(name));

	[Fact]
	public void A_path_traversal_attempt_cannot_escape_the_results_directory()
	{
		// A run name comes from a mod name or a command line, so it can be
		// anything at all.
		string safe = PortableFileName.MakeSafe("../../etc/passwd");

		XAssert.DoesNotContain("/", safe);
		XAssert.DoesNotContain("\\", safe);
		XAssert.Equal(".._.._etc_passwd", safe);
	}

	[Fact]
	public void Long_names_are_capped()
	{
		string safe = PortableFileName.MakeSafe(new string('x', 500));

		XAssert.Equal(PortableFileName.MaxComponentLength, safe.Length);
	}

	[Fact]
	public void Truncation_never_splits_a_surrogate_pair()
	{
		// A lone surrogate in a file name produces mojibake or an outright
		// error depending on the filesystem.
		string name = new string('x', PortableFileName.MaxComponentLength - 1) + "\U0001F480";
		string safe = PortableFileName.MakeSafe(name);

		XAssert.False(char.IsHighSurrogate(safe[^1]), "truncation left a lone high surrogate");
		XAssert.True(safe.Length <= PortableFileName.MaxComponentLength);
	}

	[Fact]
	public void An_unsafe_replacement_character_is_rejected()
	{
		XAssert.Throws<ArgumentException>(() => PortableFileName.MakeSafe("x", '/'));
		XAssert.Throws<ArgumentException>(() => PortableFileName.MakeSafe("x", ':'));
	}

	[Fact]
	public void Unique_names_are_deduplicated_case_insensitively()
	{
		// Windows and macOS default to case-insensitive filesystems, Linux
		// does not. Without this, two tests differing only in case give one
		// file on a Mac and two in Linux CI.
		IReadOnlyList<string> unique = PortableFileName.MakeUnique(["Test", "test", "TEST"]);

		XAssert.Equal(3, unique.Distinct(StringComparer.OrdinalIgnoreCase).Count());
	}

	[Fact]
	public void Unique_names_preserve_input_order_and_leave_the_first_alone()
	{
		IReadOnlyList<string> unique = PortableFileName.MakeUnique(["alpha", "beta", "alpha"]);

		XAssert.Equal("alpha", unique[0]);
		XAssert.Equal("beta", unique[1]);
		XAssert.NotEqual("alpha", unique[2]);
	}

	[Fact]
	public void Unique_names_are_also_sanitized()
		=> XAssert.All(PortableFileName.MakeUnique(["a/b", "a:b"]), n => XAssert.DoesNotContain("/", n));

	[Fact]
	public void Sanitized_names_actually_round_trip_through_the_filesystem()
	{
		// The rules are theory until a file appears on disk with that name.
		string dir = Path.Combine(Path.GetTempPath(), "testaria-" + Guid.NewGuid().ToString("N"));
		System.IO.Directory.CreateDirectory(dir);

		try {
			foreach (string nasty in new[] { "CON", "weird<>:name", "trailing...", "a/b" }) {
				string safe = PortableFileName.MakeSafe(nasty);
				string full = Path.Combine(dir, safe + ".xml");

				File.WriteAllText(full, "ok");

				XAssert.True(File.Exists(full), $"{nasty} sanitized to {safe} but did not round-trip");
				XAssert.Equal(safe + ".xml", Path.GetFileName(full));
			}
		}
		finally {
			System.IO.Directory.Delete(dir, recursive: true);
		}
	}
}

public class ResultsLocationTests
{
	[Fact]
	public void Results_live_under_the_save_directory()
	{
		// ModUploadRules rule 2 forbids a published mod writing outside the
		// save and config directories.
		string path = ResultsLocation.ForRun("/saves", "MyMod");

		XAssert.StartsWith(Path.Combine("/saves", ResultsLocation.DirectoryName), path);
	}

	[Fact]
	public void Paths_are_composed_with_the_platform_separator_not_a_hardcoded_one()
	{
		string path = ResultsLocation.ForRun("base", "MyMod");

		XAssert.Equal(Path.Combine("base", "Testaria", "MyMod.xml"), path);
		XAssert.Contains(Path.DirectorySeparatorChar, path);
	}

	[Fact]
	public void A_hostile_run_name_cannot_walk_out_of_the_results_directory()
	{
		// A relative root, because this is the one test here that compares a
		// Path.Combine result against a Path.GetDirectoryName one, and those
		// two disagree about a foreign separator: given "/saves", Combine
		// keeps the slash and GetDirectoryName rewrites it to a backslash on
		// Windows. The run name is what is under test, and it cannot escape
		// from a relative root any more than an absolute one.
		string path = ResultsLocation.ForRun("base", "../../etc/passwd");
		string expectedDir = ResultsLocation.Directory("base");

		XAssert.Equal(expectedDir, Path.GetDirectoryName(path));
	}

	[Fact]
	public void A_reserved_run_name_is_escaped()
		=> XAssert.Equal("_NUL.xml", Path.GetFileName(ResultsLocation.ForRun("/saves", "NUL")));

	[Fact]
	public void An_extension_without_a_dot_is_rejected()
		=> XAssert.Throws<ArgumentException>(() => ResultsLocation.ForRun("/saves", "run", "xml"));

	[Fact]
	public void A_null_save_directory_is_rejected()
		=> XAssert.Throws<ArgumentNullException>(() => ResultsLocation.Directory(null!));

	[Fact]
	public void An_empty_save_directory_is_rejected()
		=> XAssert.Throws<ArgumentException>(() => ResultsLocation.Directory(string.Empty));
}
