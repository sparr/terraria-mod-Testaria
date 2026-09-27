using System.Linq.Expressions;
using System.Reflection;

namespace Testaria;

/// <summary>
/// Calls a test method without a reflection boundary between the runner and the
/// body.
/// <para/>
/// <c>MethodInfo.Invoke</c> would do the same job, and the reason not to use it
/// is what it does to an exception on the way out. Reflection catches whatever
/// the body threw and hands it back, which raises a second first-chance
/// exception for the same object, at the invoke rather than at the throw.
/// tModLoader logs first-chance exceptions, so a skip, which is a throw, was
/// logged from a stack that no longer named <see cref="Assert.Skip"/> and could
/// not be recognised as ours. Measured on a seven-mod sweep: suppressing the
/// throw alone left 3 of 245 skips in the log, and they were all that second
/// event.
/// <para/>
/// A compiled expression is a direct call, so the body's exception propagates
/// once, through frames that name the test. Nothing else about it is an
/// optimisation, and it is not worth treating as one: the compile happens once
/// per method and the win is legibility of the log, not speed.
/// </summary>
public static class TestInvoker
{
	/// <summary>
	/// An invoker for one method, taking the instance to call it on, or
	/// <see langword="null"/> for a static method, and its arguments in
	/// declaration order.
	/// <para/>
	/// <see langword="null"/> when the signature cannot be bound this way, which
	/// a caller should read as "use reflection instead" rather than as an error.
	/// A by-ref or pointer parameter is the case that does it, and a test
	/// framework refusing to run such a test would be a worse answer than a
	/// reflection call.
	/// </summary>
	public static Func<object?, object?[], object?>? For(MethodInfo method)
	{
		ArgumentNullException.ThrowIfNull(method);

		try {
			ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
			ParameterExpression arguments = Expression.Parameter(typeof(object?[]), "arguments");
			ParameterInfo[] parameters = method.GetParameters();
			var bound = new Expression[parameters.Length];

			for (int i = 0; i < parameters.Length; i++) {
				bound[i] = Expression.Convert(
					Expression.ArrayIndex(arguments, Expression.Constant(i)),
					parameters[i].ParameterType);
			}

			Expression call = Expression.Call(
				method.IsStatic ? null : Expression.Convert(instance, method.DeclaringType!),
				method,
				bound);

			// A void method still has to return something, since the caller
			// reads a coroutine's enumerator from the same place.
			Expression body = method.ReturnType == typeof(void)
				? Expression.Block(call, Expression.Constant(null, typeof(object)))
				: Expression.Convert(call, typeof(object));

			return Expression.Lambda<Func<object?, object?[], object?>>(body, instance, arguments)
				.Compile();
		}
		catch (Exception) {
			return null;
		}
	}
}
