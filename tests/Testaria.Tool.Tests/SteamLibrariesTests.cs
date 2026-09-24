namespace Testaria.Tool.Tests;

public class SteamLibrariesTests
{
	// Trimmed from a real libraryfolders.vdf: two libraries, the second one
	// being where this machine actually keeps tModLoader.
	private const string TwoLibraries = """
		"libraryfolders"
		{
			"0"
			{
				"path"		"/home/someone/.local/share/Steam"
				"label"		""
				"contentid"		"521520727115805370"
				"apps"
				{
					"228980"		"603730734"
				}
			}
			"1"
			{
				"path"		"/home/someone/Games/Steam"
				"label"		"~/Games/Steam"
				"apps"
				{
					"1281930"		"1235590719"
				}
			}
		}
		""";

	[Fact]
	public void Every_library_is_found_not_just_the_first()
	{
		// The bug this exists to stop: probing only the primary library and
		// then reporting that tModLoader is not installed.
		XAssert.Equal(
			["/home/someone/.local/share/Steam", "/home/someone/Games/Steam"],
			SteamLibraries.Parse(TwoLibraries));
	}

	[Fact]
	public void Nothing_in_is_nothing_out()
	{
		XAssert.Empty(SteamLibraries.Parse(null));
		XAssert.Empty(SteamLibraries.Parse(""));
		XAssert.Empty(SteamLibraries.Parse("   "));
	}

	[Fact]
	public void A_file_with_no_libraries_yields_none()
		=> XAssert.Empty(SteamLibraries.Parse("\"libraryfolders\"\n{\n}\n"));

	[Fact]
	public void Windows_paths_are_unescaped()
	{
		// VDF doubles backslashes, so the raw value is "D:\\SteamLibrary" and
		// using it as written would look for a directory that is not there.
		XAssert.Equal([@"D:\SteamLibrary"], SteamLibraries.Parse("\"path\"\t\t\"D:\\\\SteamLibrary\""));
	}

	[Fact]
	public void An_app_sits_under_steamapps_common()
		=> XAssert.Equal(
			Path.Combine("/lib", "steamapps", "common", "tModLoader"),
			SteamLibraries.AppDirectory("/lib", "tModLoader"));

	[Fact]
	public void A_steam_root_without_an_index_is_skipped_rather_than_fatal()
	{
		// A machine with no Steam at all must still get an ordinary "not
		// found", never an exception out of the lookup.
		XAssert.Empty(SteamLibraries.Discover([Path.Combine(Path.GetTempPath(), "testaria-no-steam-here")]));
	}

	[Fact]
	public void Libraries_are_read_from_a_real_index_file()
	{
		string root = Path.Combine(Path.GetTempPath(), "testaria-steam-" + Path.GetRandomFileName());
		Directory.CreateDirectory(Path.Combine(root, "steamapps"));
		File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"), TwoLibraries);

		try {
			XAssert.Equal(
				["/home/someone/.local/share/Steam", "/home/someone/Games/Steam"],
				SteamLibraries.Discover([root]));
		}
		finally {
			Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public void The_same_library_listed_twice_is_offered_once()
	{
		string root = Path.Combine(Path.GetTempPath(), "testaria-steam-" + Path.GetRandomFileName());
		string other = Path.Combine(Path.GetTempPath(), "testaria-steam-" + Path.GetRandomFileName());

		foreach (string dir in new[] { root, other }) {
			Directory.CreateDirectory(Path.Combine(dir, "steamapps"));
			File.WriteAllText(Path.Combine(dir, "steamapps", "libraryfolders.vdf"), TwoLibraries);
		}

		try {
			// Both roots name the same two libraries, which is what happens
			// when ~/.steam/steam is a symlink to ~/.local/share/Steam.
			XAssert.Equal(2, SteamLibraries.Discover([root, other]).Count());
		}
		finally {
			Directory.Delete(root, recursive: true);
			Directory.Delete(other, recursive: true);
		}
	}
}
