using System.Reflection;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// Whether a mod's content can be copied without its copies sharing state.
/// <para/>
/// tModLoader works this out for every registered type and then only writes a
/// line in the log:
/// <code>
/// X has reference fields (...) that may not be safe to share between clones.
/// </code>
/// The condition is already computed, by
/// <c>Cloning.IsCloneable</c> behind <c>ModType.IsCloneable</c> and
/// <c>GlobalType.IsCloneable</c>. Nothing asserts it, and a log warning at load
/// is read by nobody once a mod works.
/// <para/>
/// What it costs to ignore is a class of bug whose symptoms appear nowhere near
/// its cause. A <c>ModItem</c> that is not cloneable has items in the world
/// sharing one mutable object: two of the "same" sword hold one list between
/// them, and splitting a stack splits nothing. The fix is usually one attribute,
/// <c>Terraria.ModLoader.CloneByReference</c>, which is tModLoader's own and
/// says "sharing this is fine".
/// <para/>
/// Read rather than recomputed, deliberately. A source-level guess at which
/// fields need deep copying was tried while surveying the corpus and was wrong:
/// it matched local variables inside method bodies and produced seventeen
/// candidates where the loader itself reports one. The property is the
/// authority, and reading it is the whole of the work.
/// </summary>
public static class CloneSweep
{
	/// <summary>
	/// Every piece of content whose cloneability tModLoader computes, as
	/// <c>Mod/InternalName</c>.
	/// <para/>
	/// The entity-bound kinds and the globals that hook them, which is where
	/// cloning happens: an entity is copied, and a per-entity instance is copied
	/// with it. <c>ModSystem</c> and friends are not here because they are not
	/// cloned.
	/// </summary>
	public static IEnumerable<string> Every()
		=> ContentSweep.Every<ModItem>()
			.Concat(ContentSweep.Every<ModNPC>())
			.Concat(ContentSweep.Every<ModProjectile>())
			.Concat(ContentSweep.Every<GlobalItem>())
			.Concat(ContentSweep.Every<GlobalNPC>())
			.Concat(ContentSweep.Every<GlobalProjectile>())
			.Order();

	/// <summary>
	/// Asserts that one piece of content reports itself cloneable.
	/// <para/>
	/// Reading <c>IsCloneable</c> is safe on the loader's own live instance:
	/// it is computed over the type and writes nothing. That matters here,
	/// because unlike the property sweep this check runs against registered
	/// content rather than against an object it made for itself.
	/// <para/>
	/// Found by name rather than through an interface, because there is none to
	/// use: <c>ModType&lt;TEntity&gt;</c> and <c>GlobalType&lt;TGlobal,
	/// TEntity&gt;</c> each declare <c>IsCloneable</c> separately and share no
	/// ancestor that has it. Daybreak's own contract enforcer reaches it the
	/// same way, which is some comfort that there is no better route.
	/// <para/>
	/// A skip when the property cannot be found at all, rather than a failure.
	/// That would mean tModLoader has moved or renamed it, which is a fact about
	/// the framework's own assumptions and not about the mod being swept.
	/// </summary>
	public static void IsCloneable(string qualified)
	{
		ILoadable content = Resolve(qualified);
		Type type = content.GetType();

		PropertyInfo? property = type.GetProperty("IsCloneable",
			BindingFlags.Public | BindingFlags.Instance);

		if (property?.GetMethod is null) {
			Assert.Skip($"{type.FullName} has no readable IsCloneable, so tModLoader has "
				+ "moved it and this check no longer knows where to look.");
		}

		object? answer;

		try {
			answer = property!.GetMethod!.Invoke(content, null);
		}
		catch (Exception bad) {
			Exception real = bad is TargetInvocationException { InnerException: { } inner } ? inner : bad;

			Assert.Skip($"{type.FullName}'s IsCloneable threw "
				+ $"{real.GetType().Name}: {real.Message}, so it has no answer to give.");
			return;
		}

		if (answer is not bool cloneable) {
			Assert.Skip($"{type.FullName}'s IsCloneable answered "
				+ $"{answer?.GetType().Name ?? "null"} rather than a bool.");
			return;
		}

		if (cloneable)
			return;

		(string own, string foreign) = Blame(type);

		// Restricted to the subject's own assembly, for the reason
		// Assert.OwnProperties gives about properties: these checks ask about
		// the code somebody wrote, not the framework it derives from.
		// tModLoader's ModNPC declares SpawnModBiomes as an int[], and
		// ModType declares Mod and Entity, so any type deriving from them
		// reports false whatever the mod did. Measured against ExampleMod:
		// blaming every type reported 16 failures, 15 of which named no field
		// the mod declared; narrowing to the mod's own fields leaves 1, which
		// is a real one.
		if (own == "none") {
			Assert.Skip($"{qualified} reports itself not cloneable, and every field "
				+ $"that could explain it belongs to tModLoader rather than to the mod: "
				+ $"{foreign}. Nothing the mod declared is at fault, so there is nothing "
				+ "here for it to answer for.");
		}

		Assert.True(cloneable,
			$"{qualified} ({type.FullName}) is not cloneable, so its copies share "
			+ "mutable state: two of the same thing in the world hold one object "
			+ $"between them.\n  fields the mod declares: {own}\n  fields tModLoader declares: {foreign}\n"
			+ "Give it a Clone override that copies the fields, or mark the ones that "
			+ "may be shared with [CloneByReference]. If the sharing is intended, the "
			+ "mod's own suite can declare it with "
			+ $"SweepExemptions.Declare(\"{qualified}\", SweepCheck.Cloning, ...)");
	}

	/// <summary>
	/// Which reference fields could be behind a type not being cloneable, split
	/// by whether the mod declared them.
	/// <para/>
	/// For the message only. tModLoader's own answer comes from
	/// <c>DeepCloning.NeedsFieldClone</c>, which is internal, so this is an
	/// approximation of the same idea and is never used to decide anything: the
	/// property decides, this only says where to look.
	/// <para/>
	/// The split matters more than the list. A type is not cloneable when
	/// <i>any</i> class in its hierarchy has reference fields and none of them
	/// overrides <c>Clone</c>, so a base class in tModLoader can make every
	/// mod's derived type report false. Naming the assembly is what lets a
	/// reader tell "your field" from "not your field".
	/// </summary>
	private static (string Own, string Foreign) Blame(Type type)
	{
		List<string> own = [];
		List<string> foreign = [];

		for (Type? at = type; at is not null && at != typeof(object); at = at.BaseType) {
			foreach (FieldInfo field in at.GetFields(BindingFlags.Instance
				| BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
				if (field.FieldType.IsValueType || field.FieldType == typeof(string))
					continue;

				if (field.GetCustomAttributes().Any(a => a.GetType().Name == nameof(CloneByReference)))
					continue;

				string described = $"{at.Name}.{field.Name} ({Short(field.FieldType)})";

				(at.Assembly == type.Assembly ? own : foreign).Add(described);
			}
		}

		return (Describe(own), Describe(foreign));

		static string Describe(List<string> fields)
			=> fields.Count == 0 ? "none" : string.Join(", ", fields.Take(8))
				+ (fields.Count > 8 ? $", ... {fields.Count} in total" : "");

		static string Short(Type type)
			=> type.IsGenericType
				? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GenericTypeArguments.Select(a => a.Name))}>"
				: type.Name;
	}

	/// <summary>
	/// One swept name, resolved back to the loader's instance.
	/// <para/>
	/// Tried against each kind in turn because the name carries the mod and the
	/// content's own name but not which kind it is, and two kinds in one mod may
	/// share a name.
	/// </summary>
	private static ILoadable Resolve(string qualified)
	{
		if (TryResolve<ModItem>(qualified, out ILoadable? found)
			|| TryResolve<ModNPC>(qualified, out found)
			|| TryResolve<ModProjectile>(qualified, out found)
			|| TryResolve<GlobalItem>(qualified, out found)
			|| TryResolve<GlobalNPC>(qualified, out found)
			|| TryResolve<GlobalProjectile>(qualified, out found)) {
			return found!;
		}

		Assert.Skip($"{qualified} no longer resolves, so there is nothing to check.");
		throw new InvalidOperationException("unreachable");
	}

	private static bool TryResolve<T>(string qualified, out ILoadable? found)
		where T : class, ILoadable, IModType
	{
		found = null;

		int split = qualified.IndexOf(ContentSweep.Separator);

		if (split <= 0)
			return false;

		if (!ModContent.TryFind(qualified[..split], qualified[(split + 1)..], out T resolved))
			return false;

		found = resolved;
		return true;
	}
}
