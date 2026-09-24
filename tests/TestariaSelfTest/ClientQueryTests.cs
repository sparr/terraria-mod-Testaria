using System.Collections;
using System.IO;
using Terraria;
using Terraria.ID;
using Testaria;

namespace TestariaSelfTest;

/// <summary>
/// Asking a client a question this suite taught it to answer.
/// <para/>
/// Until this existed, tier 3 could ask a client what tile it saw and what NPC
/// it saw, and nothing else, because those are the only two things the
/// framework itself understands. That left every mod's own synced state out of
/// reach: replicated entities, custom packets, synced fields, which is the
/// entire reason a mod has netcode.
/// </summary>
public class ClientQueryTests
{
	[NetTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator A_question_makes_the_round_trip(ITestContext ctx)
	{
		ClientLink.Request answer = ClientLink.Ask(ClientQueries.Echo, w => w.Write(1234));

		yield return Wait.Until(() => answer.Answered, "the client to echo");

		Assert.False(answer.Failed, answer.Error);
		Assert.Equal(1234, answer.Read().ReadInt32());
	}

	/// <summary>
	/// The claim that matters: the handler ran on the client, not here.
	/// <para/>
	/// A question answered by the server would be a very elaborate way of
	/// reading a local variable, and every netcode test built on it would pass
	/// while proving nothing. The netmode is the cheapest proof available that
	/// the answer came from the other process.
	/// </summary>
	[NetTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator The_answer_comes_from_the_client(ITestContext ctx)
	{
		Assert.Equal(NetmodeID.Server, Main.netMode);

		ClientLink.Request answer = ClientLink.Ask(ClientQueries.Netmode);

		yield return Wait.Until(() => answer.Answered, "the client to report its netmode");

		Assert.False(answer.Failed, answer.Error);
		Assert.Equal(NetmodeID.MultiplayerClient, answer.Read().ReadInt32());
	}

	[NetTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator Several_values_arrive_in_the_order_they_were_written(ITestContext ctx)
	{
		ClientLink.Request answer = ClientLink.Ask(ClientQueries.Several);

		yield return Wait.Until(() => answer.Answered, "the client to answer");

		Assert.False(answer.Failed, answer.Error);

		BinaryReader reader = answer.Read();

		Assert.Equal(1, reader.ReadInt32());
		Assert.Equal("two", reader.ReadString());
		Assert.Equal(3.5f, reader.ReadSingle());
		Assert.True(reader.ReadBoolean());
	}

	/// <summary>
	/// A question the client does not have must come back as an error rather
	/// than not come back at all. A test waiting on a reply that is never
	/// coming reports a timeout, which says nothing about what went wrong;
	/// this says which name was wrong and what was registered instead.
	/// </summary>
	[NetTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator An_unknown_question_is_refused_rather_than_ignored(ITestContext ctx)
	{
		ClientLink.Request answer = ClientLink.Ask("TestariaSelfTest.NoSuchQuestion");

		yield return Wait.Until(() => answer.Answered, "the client to refuse");

		Assert.True(answer.Failed, "the client should have said it does not know that question");
		Assert.Contains("NoSuchQuestion", answer.Error);
		// Names what the client does have, because the usual cause is a typo.
		Assert.Contains(ClientQueries.Echo, answer.Error);
	}

	[NetTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator A_handler_that_throws_reports_what_it_threw(ITestContext ctx)
	{
		ClientLink.Request answer = ClientLink.Ask(ClientQueries.Boom);

		yield return Wait.Until(() => answer.Answered, "the client to report the exception");

		Assert.True(answer.Failed);
		Assert.Contains("InvalidOperationException", answer.Error);
		Assert.Contains("deliberate", answer.Error);
	}

	/// <summary>
	/// Reading a failed answer fails the test rather than handing back an
	/// empty reader, which would be misread as a legitimate "no".
	/// </summary>
	[NetTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator Reading_a_refused_answer_fails_loudly(ITestContext ctx)
	{
		ClientLink.Request answer = ClientLink.Ask("TestariaSelfTest.StillNoSuchQuestion");

		yield return Wait.Until(() => answer.Answered, "the client to refuse");

		AssertionException thrown = Assert.Throws<AssertionException>(() => answer.Read());

		Assert.Contains("could not answer", thrown.Message);
	}

	/// <summary>
	/// Asking without ever waiting is stopped before it can wedge the run.
	/// <para/>
	/// The mistake this guards is easy and its symptoms point nowhere near its
	/// cause. A question asked from inside a <c>Wait.Until</c> predicate is
	/// asked again on every tick, which fills the send buffer and hangs the
	/// server's main thread; measured, the watchdog reported "Server hung for
	/// more than 10 seconds" and four neighbouring tests failed with timeouts.
	/// Failing the test that did it, by name, is worth a great deal more.
	/// </summary>
	[NetTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator Asking_without_ever_waiting_is_refused(ITestContext ctx)
	{
		AssertionException thrown = Assert.Throws<AssertionException>(() => {
			// One more than the limit, without waiting for any of them.
			for (int i = 0; i <= ClientLink.MaxQuestionsInFlight; i++)
				ClientLink.Ask(ClientQueries.Echo, w => w.Write(i));
		});

		Assert.Contains("waiting for an answer", thrown.Message);
		Assert.Contains("Wait.Until", thrown.Message);

		yield break;
	}

	/// <summary>
	/// Two questions in flight at once must not be answered into each other.
	/// </summary>
	[NetTest(Band = Band.Cavern, Timeout = 600)]
	public IEnumerator Questions_in_flight_together_keep_their_own_answers(ITestContext ctx)
	{
		ClientLink.Request first = ClientLink.Ask(ClientQueries.Echo, w => w.Write(111));
		ClientLink.Request second = ClientLink.Ask(ClientQueries.Echo, w => w.Write(222));

		yield return Wait.Until(() => first.Answered && second.Answered, "both answers");

		Assert.NotEqual(first.Id, second.Id);
		Assert.Equal(111, first.Read().ReadInt32());
		Assert.Equal(222, second.Read().ReadInt32());
	}
}
