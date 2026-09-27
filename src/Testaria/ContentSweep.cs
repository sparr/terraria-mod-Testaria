using System.Reflection;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// The content every loaded mod registers, as cases a suite can enumerate.
/// <para/>
/// The companion to <see cref="TypeSweep"/>, and for the same reason. An
/// invariant like "every NPC has more than no life" is true of any mod that
/// registers an NPC, so binding it to one subject wastes it. This turns "every
/// NPC in every loaded mod" into names a <c>[CaseSource]</c> can enumerate, so
/// each one arrives as its own result and a failure names the NPC rather than
/// the loop it was found in.
/// <para/>
/// Names rather than the content itself, because a case label should read as
/// something and a filter should be able to match it.
/// </summary>
public static class ContentSweep
{
	/// <inheritdoc cref="TypeSweep.Separator"/>
	public const char Separator = TypeSweep.Separator;

	/// <summary>
	/// Every piece of content of a kind, across every loaded mod, as
	/// <c>Mod/InternalName</c>.
	/// <para/>
	/// The harness and any suite are left out, for the reason
	/// <see cref="TypeSweep.EveryConstructibleType"/> gives: a suite's own
	/// content is not the subject, and neither is the framework's.
	/// </summary>
	public static IEnumerable<string> Every<T>() where T : ILoadable, IModType
		=> ModLoader.Mods
			.Where(mod => !IsHarness(mod.Name))
			.SelectMany(mod => mod.GetContent<T>()
				.Select(content => $"{mod.Name}{Separator}{content.Name}"))
			.Order();

	/// <summary>
	/// One of those, resolved, given the name <see cref="Every{T}"/> produced.
	/// <para/>
	/// A skip rather than a failure when it no longer resolves: the mod list
	/// can differ between the discovery that produced the name and the run that
	/// consumes it, and that is a fact about the machine rather than about the
	/// mod.
	/// </summary>
	public static T Require<T>(string qualified) where T : class, ILoadable, IModType
	{
		int split = qualified.IndexOf(Separator);

		if (split <= 0)
			Assert.Fail($"'{qualified}' does not name a mod and a piece of content separated by '{Separator}'.");

		string modName = qualified[..split];
		string contentName = qualified[(split + 1)..];

		if (!ModContent.TryFind(modName, contentName, out T found))
			Assert.Skip($"{qualified} no longer resolves, so there is nothing to check.");

		return found;
	}

	/// <summary>
	/// Whether a piece of content overrides one of the hooks named, by exact
	/// signature.
	/// <para/>
	/// The signature is not optional, and the corpus is why. <c>GetMethod(name)</c>
	/// with no parameter types throws <c>AmbiguousMatchException</c> as soon as a
	/// type has an overload, and DragonLens has a <c>ModSystem</c> carrying its
	/// own generic <c>NetSend&lt;T&gt;(int, int)</c> alongside the
	/// <c>NetSend(BinaryWriter)</c> it inherits. Asking by name alone turned
	/// three case sources into discovery errors, which is a failure of the sweep
	/// rather than a finding about the mod.
	/// <para/>
	/// Either hook counts, not both. tModLoader already refuses a type that
	/// overrides one of a required pair alone, so finding one is finding both.
	/// </summary>
	public static bool Overrides<T>(string qualified, params (string Name, Type[] Parameters)[] hooks)
		where T : class, ILoadable, IModType
	{
		int split = qualified.IndexOf(Separator);

		if (split <= 0 || !ModContent.TryFind(qualified[..split], qualified[(split + 1)..], out T found))
			return false;

		Type type = found.GetType();

		foreach ((string name, Type[] parameters) in hooks) {
			MethodInfo? hook;

			try {
				hook = type.GetMethod(name, parameters);
			}
			catch {
				// A type whose hierarchy cannot be reflected over is not a
				// subject, and is certainly not a finding about the hook.
				continue;
			}

			if (hook is not null && hook.DeclaringType != typeof(T))
				return true;
		}

		return false;
	}

	/// <summary>The mod a swept name belongs to, for a message that should say so.</summary>
	public static string ModOf(string qualified)
	{
		int split = qualified.IndexOf(Separator);

		return split <= 0 ? qualified : qualified[..split];
	}

	/// <summary>
	/// Whether a mod is a subject worth sweeping, rather than this framework, a
	/// suite built on it, or tModLoader's own built-in one.
	/// <para/>
	/// Public because every sweep needs the same answer and three copies of it
	/// would drift. Recognised by name, which is crude and is the only signal
	/// available from here.
	/// </summary>
	public static bool IsSubjectMod(string modName) => !IsHarness(modName);

	private static bool IsHarness(string modName)
		=> modName == "ModLoader"
			|| modName == nameof(Testaria)
			|| modName.EndsWith("Test", StringComparison.Ordinal)
			|| modName.EndsWith("Tests", StringComparison.Ordinal);
}
