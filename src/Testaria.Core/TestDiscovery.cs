using System.Collections;
using System.Reflection;

namespace Testaria;

/// <summary>
/// Finds tests by reflection, expanding parameterised ones into a case each.
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
			bool typeMutates = type.GetCustomAttribute<MutatesGlobalStateAttribute>() is not null;
			bool typeRealTime = type.GetCustomAttribute<RealTimeAttribute>() is not null;
			bool typeStartPaused = type.GetCustomAttribute<StartPausedAttribute>() is not null;
			int? typeSeed = type.GetCustomAttribute<SeedAttribute>()?.Seed;

			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
				if (method.GetCustomAttribute<TestariaTestAttribute>() is not TestariaTestAttribute marker)
					continue;

				string location = $"{type.FullName}.{method.Name}";

				if (Validate(type, method, location) is TestDiscoveryError error) {
					errors.Add(error);
					continue;
				}

				bool freshWorld = typeFreshWorld || method.GetCustomAttribute<FreshWorldAttribute>() is not null;
				bool mutates = typeMutates || method.GetCustomAttribute<MutatesGlobalStateAttribute>() is not null;
				bool realTime = typeRealTime || method.GetCustomAttribute<RealTimeAttribute>() is not null;
				bool startPaused = typeStartPaused || method.GetCustomAttribute<StartPausedAttribute>() is not null;
				int? seed = method.GetCustomAttribute<SeedAttribute>()?.Seed ?? typeSeed;
				bool wantsContext = WantsContext(method);
				int dataParameters = method.GetParameters().Length - (wantsContext ? 1 : 0);

				TestCase Build(IReadOnlyList<object?> arguments, string? skip = null) => new() {
					Method = method,
					Arguments = arguments,
					Tier = marker.Tier,
					BodyKind = typeof(IEnumerator).IsAssignableFrom(method.ReturnType) ? TestBodyKind.Coroutine : TestBodyKind.Immediate,
					WantsContext = wantsContext,
					SkipReason = skip ?? marker.Skip,
					TimeoutTicks = marker.Timeout,
					Box = marker is IBoxedTest boxed ? boxed.ToRequest() : null,
					FreshWorld = freshWorld,
					MutatesGlobalState = mutates,
					RealTime = realTime,
					StartPaused = startPaused,
					Seed = seed,
				};

				if (dataParameters == 0) {
					tests.Add(Build([], null));
					continue;
				}

				ExpandCases(type, method, location, dataParameters, Build, tests, errors);
			}
		}

		return new DiscoveryResult { Tests = tests, Errors = errors };
	}

	/// <summary>
	/// Turns a parameterised method into one case per argument set.
	/// <para/>
	/// A source that fails is reported rather than skipped. A parameterised
	/// test whose cases silently fail to materialise leaves a suite that looks
	/// smaller than it is, which is the same hazard as a malformed test
	/// vanishing.
	/// </summary>
	private static void ExpandCases(
		Type type,
		MethodInfo method,
		string location,
		int dataParameters,
		Func<IReadOnlyList<object?>, string?, TestCase> build,
		List<TestCase> tests,
		List<TestDiscoveryError> errors)
	{
		List<object?[]> cases = [];
		int sources = 0;

		foreach (CaseAttribute inline in method.GetCustomAttributes<CaseAttribute>()) {
			sources++;
			cases.Add(inline.Data);
		}

		foreach (CaseSourceAttribute source in method.GetCustomAttributes<CaseSourceAttribute>()) {
			sources++;

			try {
				cases.AddRange(ReadMemberData(type, source.MemberName));
			}
			catch (Exception ex) {
				errors.Add(Error(location, $"The data source '{source.MemberName}' could not be read: {ex.GetType().Name}: {ex.Message}"));
				return;
			}
		}

		if (sources == 0) {
			errors.Add(Error(location, $"This test takes {dataParameters} argument(s) but no [Case] or [CaseSource] supplies any."));
			return;
		}

		if (cases.Count == 0) {
			// Reported as skipped rather than as an error or as nothing. A
			// source can legitimately be empty, when the content it enumerates
			// is not installed, and a test that quietly disappears would leave
			// the suite looking complete.
			tests.Add(build([], "Its data source produced no cases, so there was nothing to run."));
			return;
		}

		int index = 0;
		foreach (object?[] arguments in cases) {
			index++;

			if (arguments.Length != dataParameters) {
				errors.Add(Error(location, $"Case {index} supplies {arguments.Length} argument(s) but the test takes {dataParameters}."));
				continue;
			}

			tests.Add(build(arguments, null));
		}
	}

	/// <summary>Reads a static field, property or parameterless method as case data.</summary>
	private static IEnumerable<object?[]> ReadMemberData(Type type, string memberName)
	{
		const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

		object? value = type.GetProperty(memberName, Flags)?.GetValue(null)
			?? type.GetField(memberName, Flags)?.GetValue(null)
			?? type.GetMethod(memberName, Flags, Type.EmptyTypes)?.Invoke(null, null)
			?? throw new MissingMemberException($"no static property, field or parameterless method named '{memberName}' on {type.Name}");

		// A string is IEnumerable, of characters, so an unguarded check would
		// quietly turn one into a case per letter rather than reporting it.
		if (value is string or not IEnumerable)
			throw new InvalidCastException($"'{memberName}' is {value.GetType().Name}, not a sequence of object arrays");

		var rows = (IEnumerable)value;

		List<object?[]> cases = [];
		foreach (object? row in rows) {
			cases.Add(row switch {
				object?[] array => array,
				// A single-argument source is tidier written without the
				// surrounding array, so accept both shapes.
				null => [null],
				_ => [row],
			});
		}

		return cases;
	}

	private static bool WantsContext(MethodInfo method)
	{
		ParameterInfo[] parameters = method.GetParameters();

		return parameters.Length > 0 && typeof(ITestContext).IsAssignableFrom(parameters[0].ParameterType);
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
		bool wantsContext = WantsContext(method);
		int dataParameters = parameters.Length - (wantsContext ? 1 : 0);

		// A context may only lead. Anywhere else it is data the runner cannot
		// supply, and saying so beats a confusing argument count error later.
		for (int i = wantsContext ? 1 : 0; i < parameters.Length; i++) {
			if (typeof(ITestContext).IsAssignableFrom(parameters[i].ParameterType))
				return Error(location, $"An ITestContext must be the first parameter, but one appears at position {i + 1}.");
		}

		bool hasData = method.GetCustomAttributes<CaseAttribute>().Any()
			|| method.GetCustomAttributes<CaseSourceAttribute>().Any();

		if (dataParameters == 0 && hasData)
			return Error(location, "This test has [Case] or [CaseSource] but takes no arguments to receive them.");

		return null;
	}

	private static TestDiscoveryError Error(string location, string message)
		=> new() { Location = location, Message = message };
}
