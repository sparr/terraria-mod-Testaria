using System.IO;

namespace Testaria.Tests;

/// <summary>
/// The registry behind the questions a test mod teaches a client to answer.
/// <para/>
/// Every test here clears the registry first and last. It is process-wide
/// static, because the game gives one registry per load, and xUnit runs these
/// in parallel; they are collected into one non-parallel collection for that
/// reason rather than because they are slow.
/// </summary>
[Collection(nameof(ClientQueryTests))]
[CollectionDefinition(nameof(ClientQueryTests), DisableParallelization = true)]
public class ClientQueryTests : IDisposable
{
	public ClientQueryTests() => ClientQuery.Clear();

	public void Dispose() => ClientQuery.Clear();

	private static QueryAnswer Invoke(string name, Action<BinaryWriter>? arguments = null)
	{
		using var buffer = new MemoryStream();
		using var writer = new BinaryWriter(buffer);

		arguments?.Invoke(writer);
		writer.Flush();

		return ClientQuery.Invoke(name, buffer.ToArray());
	}

	[Fact]
	public void A_registered_question_is_answered()
	{
		ClientQuery.Register("A", (_, answer) => answer.Write(42));

		QueryAnswer result = Invoke("A");

		XAssert.False(result.Failed);
		XAssert.Equal(42, new BinaryReader(new MemoryStream(result.Payload)).ReadInt32());
	}

	[Fact]
	public void Arguments_reach_the_handler_in_the_order_they_were_written()
	{
		ClientQuery.Register("Add", (arguments, answer) => {
			int left = arguments.ReadInt32();
			int right = arguments.ReadInt32();
			answer.Write(left + right);
		});

		QueryAnswer result = Invoke("Add", w => { w.Write(3); w.Write(4); });

		XAssert.Equal(7, new BinaryReader(new MemoryStream(result.Payload)).ReadInt32());
	}

	[Fact]
	public void A_question_with_no_answer_is_still_an_answer()
	{
		// "It did not throw" is a legitimate thing for a handler to report.
		ClientQuery.Register("Quiet", (_, _) => { });

		QueryAnswer result = Invoke("Quiet");

		XAssert.False(result.Failed);
		XAssert.Empty(result.Payload);
	}

	[Fact]
	public void An_unregistered_name_comes_back_as_an_error_not_a_silence()
	{
		// The whole point: a test waiting on a reply that is never coming
		// reports a timeout, which says nothing about what went wrong.
		ClientQuery.Register("Present", (_, answer) => answer.Write(1));

		QueryAnswer result = Invoke("Absent");

		XAssert.True(result.Failed);
		XAssert.Contains("Absent", result.Error);
		// Names what is registered, because the usual cause is a typo and the
		// list is how somebody spots it.
		XAssert.Contains("Present", result.Error);
	}

	[Fact]
	public void An_empty_registry_says_the_test_mod_probably_did_not_load_here()
	{
		QueryAnswer result = Invoke("Anything");

		XAssert.True(result.Failed);
		XAssert.Contains("did not load", result.Error);
	}

	[Fact]
	public void A_handler_that_throws_reports_what_it_threw()
	{
		ClientQuery.Register("Boom", (_, _) => throw new InvalidOperationException("deliberate"));

		QueryAnswer result = Invoke("Boom");

		XAssert.True(result.Failed);
		XAssert.Contains("InvalidOperationException", result.Error);
		XAssert.Contains("deliberate", result.Error);
	}

	[Fact]
	public void A_handler_reading_past_its_arguments_is_an_error_rather_than_a_crash()
	{
		ClientQuery.Register("Greedy", (arguments, answer) => answer.Write(arguments.ReadInt32()));

		// Sent nothing, so the read runs off the end.
		QueryAnswer result = Invoke("Greedy");

		XAssert.True(result.Failed);
		XAssert.Contains("Greedy", result.Error);
	}

	[Fact]
	public void An_answer_over_the_packet_limit_is_refused_rather_than_sent()
	{
		// Terraria's packets carry a 16 bit length. An answer that overran it
		// would corrupt the stream for everything after it, which is a far
		// worse failure than this one.
		ClientQuery.Register("Huge", (_, answer) => answer.Write(new byte[ClientQuery.MaxAnswerBytes + 1]));

		QueryAnswer result = Invoke("Huge");

		XAssert.True(result.Failed);
		XAssert.Contains("limit", result.Error);
	}

	[Fact]
	public void An_answer_exactly_at_the_limit_is_allowed()
	{
		ClientQuery.Register("Big", (_, answer) => answer.Write(new byte[ClientQuery.MaxAnswerBytes]));

		XAssert.False(Invoke("Big").Failed);
	}

	[Fact]
	public void Registering_one_name_twice_is_refused()
	{
		// Two mods quietly fighting over one name would make a test's answer
		// depend on load order.
		ClientQuery.Register("Taken", (_, answer) => answer.Write(1));

		InvalidOperationException thrown = XAssert.Throws<InvalidOperationException>(
			() => ClientQuery.Register("Taken", (_, answer) => answer.Write(2)));

		XAssert.Contains("already registered", thrown.Message);
	}

	[Fact]
	public void Names_are_case_sensitive_and_listed_in_order()
	{
		ClientQuery.Register("b", (_, _) => { });
		ClientQuery.Register("A", (_, _) => { });
		ClientQuery.Register("a", (_, _) => { });

		XAssert.Equal(["A", "a", "b"], ClientQuery.Names);
		XAssert.True(ClientQuery.IsRegistered("a"));
		XAssert.False(ClientQuery.IsRegistered("c"));
	}

	[Fact]
	public void Clearing_forgets_everything()
	{
		ClientQuery.Register("Gone", (_, _) => { });

		ClientQuery.Clear();

		XAssert.Empty(ClientQuery.Names);
		XAssert.False(ClientQuery.IsRegistered("Gone"));
	}

	[Fact]
	public void A_nameless_registration_is_rejected()
	{
		XAssert.Throws<ArgumentException>(() => ClientQuery.Register("", (_, _) => { }));
		XAssert.Throws<ArgumentException>(() => ClientQuery.Register("  ", (_, _) => { }));
		XAssert.Throws<ArgumentNullException>(() => ClientQuery.Register("x", null!));
	}
}
