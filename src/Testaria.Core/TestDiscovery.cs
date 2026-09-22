using System.Collections;
using System.Reflection;

namespace Testaria;

/// <summary>
/// Finds tests by reflection.
/// <para/>
/// Reflection over already-loaded types rather than a VSTest adapter, because
/// mod assemblies load from memory into a per-mod <c>AssemblyLoadContext</c>
/// and no stock runner is built to enumerate that. In the game, the caller
/// passes the types tModLoader has already loaded.
/// </summary>
public static class TestDiscovery
{
	/// <summary>Scans an assembly for test methods.</summary>
	public static DiscoveryResult Discover(Assembly assembly)
	{
		ArgumentNullException.ThrowIfNull(assembly);

		return Discover(assembly.GetTypes());
	}

	/// <summary>Scans the given types for test methods.</summary>
	public static DiscoveryResult Discover(IEnumerable<Type> types)
	{
		ArgumentNullException.ThrowIfNull(types);

		List<TestCase> tests = [];
		List<TestDiscoveryError> errors = [];

		foreach (Type type in types) {
			bool typeFreshWorld = type.GetCustomAttribute<FreshWorldAttribute>() is not null;

			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
				if (method.GetCustomAttribute<TestariaTestAttribute>() is not TestariaTestAttribute marker)
					continue;

				string location = $"{type.FullName}.{method.Name}";

				if (Validate(type, method, location) is TestDiscoveryError error) {
					errors.Add(error);
					continue;
				}

				bool freshWorld = typeFreshWorld || method.GetCustomAttribute<FreshWorldAttribute>() is not null;

				tests.Add(new TestCase {
					Method = method,
					Tier = marker.Tier,
					BodyKind = typeof(IEnumerator).IsAssignableFrom(method.ReturnType) ? TestBodyKind.Coroutine : TestBodyKind.Immediate,
					WantsContext = method.GetParameters().Length == 1,
					SkipReason = marker.Skip,
					TimeoutTicks = marker.Timeout,
					// A box is granted regardless of FreshWorld. The two are
					// orthogonal: a fresh world isolates a test from other
					// tests and from world-global state, while a box gives it
					// a defined, bounded place to work and the box-relative
					// coordinates that go with it. Withholding the box left a
					// fresh-world test with an empty Interior and no usable
					// workspace at all.
					Box = marker is GameTestAttribute game ? game.ToRequest() : null,
					FreshWorld = freshWorld,
				});
			}
		}

		return new DiscoveryResult { Tests = tests, Errors = errors };
	}

	private static TestDiscoveryError? Validate(Type type, MethodInfo method, string location)
	{
		if (!method.IsPublic)
			return Error(location, "Test methods must be public.");

		if (method.IsAbstract)
			return Error(location, "Test methods must not be abstract.");

		if (method.IsGenericMethodDefinition)
			return Error(location, "Test methods must not be generic.");

		if (!method.IsStatic) {
			if (type.IsAbstract)
				return Error(location, $"Instance test methods need an instantiable declaring type, but {type.Name} is abstract or static.");

			if (type.GetConstructor(Type.EmptyTypes) is null)
				return Error(location, $"Instance test methods need a public parameterless constructor on {type.Name}.");
		}

		if (method.ReturnType != typeof(void) && !typeof(IEnumerator).IsAssignableFrom(method.ReturnType))
			return Error(location, $"Test methods must return void or IEnumerator, but this returns {method.ReturnType.Name}. Return IEnumerator to run across ticks.");

		ParameterInfo[] parameters = method.GetParameters();
		if (parameters.Length > 1)
			return Error(location, $"Test methods take no parameters or one ITestContext, but this takes {parameters.Length}.");

		if (parameters.Length == 1 && !typeof(ITestContext).IsAssignableFrom(parameters[0].ParameterType))
			return Error(location, $"The single parameter must be assignable from ITestContext, but is {parameters[0].ParameterType.Name}.");

		return null;
	}

	private static TestDiscoveryError Error(string location, string message)
		=> new() { Location = location, Message = message };
}
