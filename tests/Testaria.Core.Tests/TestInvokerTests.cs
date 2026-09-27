using System.Reflection;

namespace Testaria.Tests;

/// <summary>
/// <see cref="TestInvoker"/>, which exists so that a test body's exception
/// reaches the runner without passing through reflection. The claim worth
/// testing is the last one: what comes out is what was thrown, not a wrapper.
/// </summary>
public class TestInvokerTests
{
	private static MethodInfo Method(string name)
		=> typeof(Subject).GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)!;

	[Fact]
	public void A_static_void_method_is_called_and_returns_null()
	{
		Subject.Calls = 0;

		object? result = TestInvoker.For(Method(nameof(Subject.StaticVoid)))!(null, []);

		XAssert.Null(result);
		XAssert.Equal(1, Subject.Calls);
	}

	[Fact]
	public void An_instance_method_is_called_on_the_instance_given()
	{
		var subject = new Subject { Value = 7 };

		XAssert.Equal(7, TestInvoker.For(Method(nameof(Subject.Read)))!(subject, []));
	}

	[Fact]
	public void Arguments_arrive_in_declaration_order()
		=> XAssert.Equal(
			"a1",
			TestInvoker.For(Method(nameof(Subject.Join)))!(new Subject(), ["a", 1]));

	[Fact]
	public void A_returned_enumerator_comes_back_as_it_is()
	{
		object? body = TestInvoker.For(Method(nameof(Subject.Coroutine)))!(new Subject(), []);

		XAssert.IsAssignableFrom<IEnumerator<int>>(body);
	}

	[Fact]
	public void What_the_body_threw_is_what_the_caller_catches()
	{
		// The whole reason this type exists. MethodInfo.Invoke would deliver a
		// TargetInvocationException here, and the runner would have to unwrap
		// it; the game's log would meanwhile have recorded the same exception
		// twice, once at the throw and once at the invoke.
		Func<object?, object?[], object?> invoke = TestInvoker.For(Method(nameof(Subject.Throws)))!;

		SkipTestException thrown = XAssert.Throws<SkipTestException>(() => invoke(new Subject(), []));

		XAssert.Equal("deliberate", thrown.Message);
	}

	[Fact]
	public void The_throwing_frame_is_named_in_the_trace()
	{
		// tModLoader recognises our skips by the frame Assert.Skip leaves, so a
		// trace that does not name the method that threw is the defect this was
		// written to remove.
		Func<object?, object?[], object?> invoke = TestInvoker.For(Method(nameof(Subject.Throws)))!;

		SkipTestException thrown = XAssert.Throws<SkipTestException>(() => invoke(new Subject(), []));

		XAssert.Contains(nameof(Subject.Throws), thrown.StackTrace);
	}

	[Fact]
	public void A_private_method_on_a_private_type_is_callable()
	{
		// Discovery accepts non-public test methods (TestDiscovery's binding
		// flags include NonPublic), and a suite in a mod that keeps its types
		// internal is the case the ecosystem calibration ran into. A compiled
		// expression that could not reach those would turn a runnable test into
		// an error, so this is the property that decides whether the fallback
		// needs to be accessibility-aware.
		MethodInfo hidden = typeof(Subject).GetMethod("Hidden", BindingFlags.NonPublic | BindingFlags.Instance)!;

		XAssert.Equal(42, TestInvoker.For(hidden)!(new Subject(), []));
	}

	[Fact]
	public void A_signature_that_cannot_be_bound_is_refused_rather_than_thrown_about()
	{
		// A caller reads null as "use reflection", so this must not throw: a by
		// ref parameter is a legitimate signature that this route cannot take.
		XAssert.Null(TestInvoker.For(Method(nameof(Subject.ByRef))));
	}

	private sealed class Subject
	{
		public static int Calls;

		public int Value { get; init; }

		public static void StaticVoid() => Calls++;

		public int Read() => Value;

		public string Join(string text, int number) => text + number;

		public IEnumerator<int> Coroutine()
		{
			yield return 1;
		}

		public void Throws() => Assert.Skip("deliberate");

		public void ByRef(ref int value) => value++;

		private int Hidden() => 42;
	}
}
