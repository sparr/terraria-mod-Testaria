using Newtonsoft.Json;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace Testaria;

/// <summary>
/// Whether a mod's configuration survives being written out and read back.
/// <para/>
/// A <c>ModConfig</c> is serialized, cloned and repopulated constantly: when it
/// loads, when the config UI closes, and on every multiplayer join. A config
/// that does not come back the same has two failure modes, and neither of them
/// looks like a config bug from the outside. The player's settings quietly
/// revert. And because <c>ConfigManager</c> decides whether a reload is needed by
/// comparing the live config against exactly such a clone, a config that does not
/// round trip can answer "yes" forever, which is a mod that reloads on every
/// server join.
/// <para/>
/// This is not hypothetical, and the corpus says so in a comment. CheatSheet
/// serializes one of its configuration objects with hand-rolled
/// <c>JsonSerializerSettings</c> while still deserializing it with
/// <c>ConfigManager.serializerSettings</c>, above a note blaming tModLoader's own
/// contract resolver and a TODO about migrating to a real <c>ModConfig</c>. An
/// asymmetric serializer pairing, diagnosed and worked around rather than fixed,
/// by somebody who lost time to it.
/// <para/>
/// Nothing here touches a live config's values. Every write goes to a clone, so
/// a run cannot reset anybody's settings.
/// </summary>
public static class ConfigSweep
{
	/// <summary>
	/// Every loaded <c>ModConfig</c>, as <c>Mod/Type.Full.Name</c>.
	/// <para/>
	/// Found by walking each mod's types rather than by asking the loader,
	/// because a <c>ModConfig</c> cannot be asked for the way other content can.
	/// It is an <c>ILocalizedModType</c> and not an <c>ILoadable</c>, so
	/// <see cref="ContentSweep"/> cannot enumerate it and
	/// <c>ModContent.TryFind</c> cannot resolve it; <c>ConfigManager.Configs</c>
	/// holds the live list and is internal. What is public is
	/// <c>ModContent.GetInstance&lt;T&gt;()</c>, so the type is discovered here
	/// and the instance fetched through that.
	/// <para/>
	/// Full type names rather than internal names, for the reason
	/// <see cref="TypeSweep.ConstructibleTypes"/> gives: two types in different
	/// namespaces may share a short name.
	/// </summary>
	public static IEnumerable<string> Every()
		=> ModLoader.Mods
			.Where(mod => !IsHarness(mod.Name))
			.SelectMany(mod => ConfigTypes(mod)
				.Select(type => $"{mod.Name}{TypeSweep.Separator}{type.FullName}"))
			.Order();

	/// <summary>
	/// One config, resolved to the live instance the game is using.
	/// <para/>
	/// A skip rather than a failure when it does not resolve: the mod list can
	/// differ between the discovery that produced the name and the run that
	/// consumes it, and a config type that exists without a registered instance
	/// is one the loader chose not to autoload.
	/// </summary>
	public static ModConfig Require(string qualified)
	{
		int split = qualified.IndexOf(TypeSweep.Separator);

		if (split <= 0)
			Assert.Fail($"'{qualified}' does not name a mod and a type separated by '{TypeSweep.Separator}'.");

		string modName = qualified[..split];
		string typeName = qualified[(split + 1)..];

		if (!ModLoader.TryGetMod(modName, out Mod mod))
			Assert.Skip($"{modName} is not installed, so there is nothing to check.");

		Type? type = mod.Code.GetType(typeName);

		if (type is null || !typeof(ModConfig).IsAssignableFrom(type))
			Assert.Skip($"{modName} no longer has a config called {typeName}.");

		object? instance;

		try {
			instance = GetInstance.MakeGenericMethod(type!).Invoke(null, null);
		}
		catch (Exception bad) {
			Assert.Skip($"{qualified}'s instance could not be fetched: {Describe(bad)}.");
			throw;
		}

		// Tested rather than pattern-bound, because Assert.Skip is not declared
		// as never returning and a bound variable would read as possibly unset.
		if (instance is not ModConfig)
			Assert.Skip($"{qualified} has no registered instance, so the loader did not autoload it.");

		return (ModConfig)instance!;
	}

	/// <summary>The config types a mod declares and the loader would register.</summary>
	private static IEnumerable<Type> ConfigTypes(Mod mod)
	{
		Type[] types;

		try {
			types = mod.Code.GetTypes();
		}
		catch (System.Reflection.ReflectionTypeLoadException partial) {
			types = partial.Types.Where(type => type is not null).ToArray()!;
		}

		return types.Where(type => typeof(ModConfig).IsAssignableFrom(type)
			&& !type.IsAbstract
			&& type.FullName is not null);
	}

	/// <summary><c>ModContent.GetInstance&lt;T&gt;</c>, ready to be closed over a discovered type.</summary>
	private static readonly System.Reflection.MethodInfo GetInstance =
		typeof(ModContent).GetMethod(nameof(ModContent.GetInstance))!;

	/// <inheritdoc cref="TypeSweep.EveryConstructibleType"/>
	private static bool IsHarness(string modName)
		=> modName == "ModLoader"
			|| modName == nameof(Testaria)
			|| modName.EndsWith("Test", StringComparison.Ordinal)
			|| modName.EndsWith("Tests", StringComparison.Ordinal);

	/// <summary>Asserts that the config can be written out at all. The first rung.</summary>
	public static void SerializeReads(string qualified)
	{
		ModConfig config = Require(qualified);

		try {
			Serialize(config);
		}
		catch (Exception bad) {
			Assert.Fail($"{qualified} cannot be serialized, which threw {Describe(bad)}. "
				+ "tModLoader writes a config out whenever it changes and on every "
				+ "multiplayer join, so this config cannot be saved or synced");
		}
	}

	/// <summary>
	/// Asserts that the config can be read back into a copy of itself. The
	/// second rung.
	/// <para/>
	/// Through <c>ConfigManager.GeneratePopulatedClone</c>, which is the same
	/// path tModLoader uses, rather than a copy invented here. The point is to
	/// exercise what the game does, not to prove that some other serialization
	/// would work.
	/// </summary>
	public static void RoundTrips(string qualified)
	{
		ModConfig config = Require(qualified);

		try {
			Serialize(config);
		}
		catch (Exception bad) {
			Assert.Skip($"{qualified} cannot be serialized, which threw {Describe(bad)}, "
				+ "so there is nothing to read back.");
			return;
		}

		try {
			ConfigManager.GeneratePopulatedClone(config);
		}
		catch (Exception bad) {
			Assert.Fail($"{qualified} cannot be populated from its own serialized form, "
				+ $"which threw {Describe(bad)}. That is the path a multiplayer join "
				+ "takes, so joining a server with this mod is what breaks");
		}
	}

	/// <summary>
	/// Asserts that the copy writes out identically to the original. The third
	/// rung, and the one the other two cannot stand in for.
	/// <para/>
	/// A config that loses a field on the way in does not throw and passes the
	/// rung above: the JSON is valid, every property is assigned, nothing
	/// complains. What is gone is the setting, and the only way to see it is to
	/// write the copy out and compare.
	/// </summary>
	public static void Settles(string qualified)
	{
		ModConfig config = Require(qualified);

		string first, second;

		try {
			first = Serialize(config);
			second = Serialize(ConfigManager.GeneratePopulatedClone(config));
		}
		catch (Exception bad) {
			// The rungs above own this and will report it.
			Assert.Skip($"{qualified} could not be round tripped, which threw "
				+ $"{Describe(bad)}, so whether it settles has no answer.");
			return;
		}

		if (first == second)
			return;

		Assert.Fail($"{qualified} does not survive its own round trip. Writing it out, "
			+ "reading that into a copy, and writing the copy out gave different JSON, so "
			+ "settings change on every save and NeedsReload can never agree with itself:\n"
			+ $"  written: {Show(first)}\n"
			+ $"  rewritten: {Show(second)}");
	}

	/// <summary>
	/// Asserts that the config can be populated from nothing.
	/// <para/>
	/// Not a rung of the round trip. tModLoader does exactly this when a config
	/// file fails to load: it warns that the file was probably corrupted and then
	/// calls <c>PopulateObject("{}", config, serializerSettings)</c>. A config
	/// that throws here turns a damaged settings file into a mod that cannot
	/// load, which is the recovery path failing at the moment it is needed.
	/// <para/>
	/// Applied to a clone. Populating the live config from nothing would reset
	/// whatever the run is using.
	/// </summary>
	public static void PopulatesFromNothing(string qualified)
	{
		ModConfig config = Require(qualified);
		ModConfig spare;

		try {
			spare = ConfigManager.GeneratePopulatedClone(config);
		}
		catch (Exception bad) {
			Assert.Skip($"{qualified} could not be copied, which threw {Describe(bad)}, "
				+ "so there is nothing safe to populate.");
			return;
		}

		try {
			JsonConvert.PopulateObject("{}", spare, ConfigManager.serializerSettings);
		}
		catch (Exception bad) {
			Assert.Fail($"{qualified} cannot be populated from an empty object, which threw "
				+ $"{Describe(bad)}. tModLoader does precisely this when the config file "
				+ "fails to load, so a corrupted settings file becomes a mod that will "
				+ "not load");
		}
	}

	private static string Serialize(ModConfig config)
		=> JsonConvert.SerializeObject(config, ConfigManager.serializerSettings);

	/// <summary>Serialized JSON, truncated, with newlines flattened.</summary>
	private static string Show(string json)
	{
		const int Limit = 400;

		string flat = json.ReplaceLineEndings(" ");

		return flat.Length <= Limit ? flat : flat[..Limit] + " ...";
	}

	private static string Describe(Exception thrown)
		=> $"{thrown.GetType().Name}: {thrown.Message}";
}
