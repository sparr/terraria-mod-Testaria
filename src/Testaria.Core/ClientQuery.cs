using System.IO;

namespace Testaria;

/// <summary>
/// Questions a test mod teaches the client to answer.
/// <para/>
/// Tier 3 can ask a client what tile it sees and what NPC it sees, and those
/// are the only two things it knows how to ask, because they are the only two
/// things the framework itself understands. That leaves a mod's own synced
/// state entirely out of reach: TileProcessor replication, custom packets,
/// synced fields, the whole reason a mod has netcode at all.
/// <para/>
/// The missing piece was never the transport, which already carries a question
/// to a client and an answer back. It was an extension point. A mod's test
/// assembly is loaded on both sides, so it can register a named question here
/// during its load pass, and the server can then ask for it by name.
/// <para/>
/// Lives in the core, and touches nothing from the game, so the naming and the
/// failure paths can be tested without one.
/// </summary>
/// <example>
/// <code>
/// // In the test mod's Load(), which runs on the server and the client both.
/// ClientQuery.Register("MyMod.SeesWidget", (arguments, answer) => {
///     int x = arguments.ReadInt32(), y = arguments.ReadInt32();
///     answer.Write(MyMod.WidgetAt(x, y) is not null);
/// });
///
/// // In a [NetTest], which runs on the server.
/// ClientLink.Request seen = ClientLink.Ask("MyMod.SeesWidget", w => { w.Write(x); w.Write(y); });
/// yield return Wait.Until(() => seen.Answered, "the client to say whether it sees the widget");
/// Assert.True(seen.Read().ReadBoolean());
/// </code>
/// </example>
public static class ClientQuery
{
	/// <summary>
	/// Answers one question on the client.
	/// <para/>
	/// Reads whatever the asking side wrote, writes whatever it wants to send
	/// back. Both may be empty: a question with no arguments and an answer of
	/// "it did not throw" are both legitimate.
	/// </summary>
	/// <param name="arguments">What the server sent, positioned at the start.</param>
	/// <param name="answer">Where to write the reply.</param>
	public delegate void Handler(BinaryReader arguments, BinaryWriter answer);

	/// <summary>
	/// The largest answer that fits. Terraria's packets carry a 16 bit length,
	/// and an answer that overran it would corrupt the stream for everything
	/// after it rather than failing on its own.
	/// </summary>
	public const int MaxAnswerBytes = 32768;

	private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.Ordinal);

	/// <summary>Every question currently registered, in name order.</summary>
	public static IReadOnlyList<string> Names
	{
		get {
			lock (Handlers)
				return [.. Handlers.Keys.Order(StringComparer.Ordinal)];
		}
	}

	/// <summary>
	/// Teaches the client to answer a question.
	/// <para/>
	/// Call this from a load pass, so that it has happened on both sides
	/// before any test runs. Registering the same name twice is an error
	/// rather than a silent replacement: two mods quietly fighting over one
	/// name would make a test's answer depend on load order, which is the sort
	/// of thing that is diagnosed at three in the morning.
	/// </summary>
	/// <param name="name">
	/// A name of your own choosing. Prefix it with your mod's name: the
	/// registry is shared by every mod in the run.
	/// </param>
	/// <param name="handler">What to do when the question is asked.</param>
	public static void Register(string name, Handler handler)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(handler);

		lock (Handlers) {
			if (!Handlers.TryAdd(name, handler))
				throw new InvalidOperationException(
					$"A client query named '{name}' is already registered. Names are shared across every mod in "
					+ "the run, so prefix yours with your mod's name.");
		}
	}

	/// <summary>Whether a question of this name is registered here.</summary>
	public static bool IsRegistered(string name)
	{
		lock (Handlers)
			return name is not null && Handlers.ContainsKey(name);
	}

	/// <summary>
	/// Forgets every registered question.
	/// <para/>
	/// Called when Testaria unloads. A handler is a delegate over a test
	/// mod's assembly, so keeping one past a reload would hold that assembly
	/// alive and answer a later run with the previous build's code.
	/// </summary>
	public static void Clear()
	{
		lock (Handlers)
			Handlers.Clear();
	}

	/// <summary>
	/// Runs a registered question over the arguments it was sent.
	/// <para/>
	/// Never throws for anything the asking side could have got wrong. A
	/// question that does not exist, or a handler that throws, comes back as
	/// an answer carrying an error, because the alternative is a test that
	/// waits for a reply that is never coming and reports a timeout, which
	/// says nothing about what actually went wrong.
	/// </summary>
	public static QueryAnswer Invoke(string name, byte[] arguments)
	{
		ArgumentNullException.ThrowIfNull(arguments);

		Handler? handler;

		lock (Handlers) {
			if (name is null || !Handlers.TryGetValue(name, out handler)) {
				string known = Handlers.Count == 0
					? "nothing is registered on this side at all, which usually means the test mod did not load here"
					: "registered: " + string.Join(", ", Handlers.Keys.Order(StringComparer.Ordinal));

				return QueryAnswer.Failure($"No client query named '{name}' is registered ({known}).");
			}
		}

		try {
			using var input = new MemoryStream(arguments, writable: false);
			using var reader = new BinaryReader(input);
			using var output = new MemoryStream();
			using var writer = new BinaryWriter(output);

			handler(reader, writer);
			writer.Flush();

			byte[] payload = output.ToArray();

			return payload.Length > MaxAnswerBytes
				? QueryAnswer.Failure($"The answer to '{name}' was {payload.Length} bytes, over the {MaxAnswerBytes} byte limit.")
				: QueryAnswer.Success(payload);
		}
		catch (Exception ex) {
			// The client's stack trace is of no use on the server, but the
			// type and message are exactly what a person needs.
			return QueryAnswer.Failure($"The client query '{name}' threw {ex.GetType().Name}: {ex.Message}");
		}
	}
}

/// <summary>What a client query produced: an answer, or a reason there is none.</summary>
public readonly record struct QueryAnswer
{
	private QueryAnswer(byte[] payload, string? error)
	{
		Payload = payload;
		Error = error;
	}

	/// <summary>The bytes the handler wrote, empty when it failed.</summary>
	public byte[] Payload { get; }

	/// <summary>Why there is no answer, or null when there is one.</summary>
	public string? Error { get; }

	/// <summary>True when no answer was produced.</summary>
	public bool Failed => Error is not null;

	internal static QueryAnswer Success(byte[] payload) => new(payload, null);

	internal static QueryAnswer Failure(string error) => new([], error);
}
