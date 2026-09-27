using Terraria;
using Terraria.ModLoader;

namespace Testaria;

/// <summary>
/// Whether the two halves of a mod's own networking agree with each other.
/// <para/>
/// A mod writes entity state in <c>SendExtraAI</c> and reads it in
/// <c>ReceiveExtraAI</c>. The pairing is a convention: nothing checks that the
/// reader takes the same fields, in the same order, at the same widths. When it
/// does not, tModLoader notices, but only on the receiving machine, at runtime,
/// in multiplayer, and then swallows it after writing a line to the log:
/// <code>
/// IOException: Read underflow 4 of 12 bytes in ReceiveExtraAI
/// </code>
/// So the entity carries wrong state on every client for the rest of the
/// session. <c>NPCLoader.ReceiveExtraAI</c> catches its own detection, which is
/// why this cannot simply call it and watch for a throw.
/// <para/>
/// InnoVault, in the surveyed corpus, built the same defense for its own
/// sub-hooks: a length prefix per payload, a seek back to fill it in, and a
/// position comparison on the way out. Its comment says what a mismatch costs,
/// and it is worth quoting because it is worse than one mod's bug: misalignment
/// pollutes "the same NPC's subsequent overrides and even other mods' ExtraAI
/// stream".
/// <para/>
/// This is a tier 1 check about netcode, which sounds wrong and is not. The
/// disagreement is between two methods in one assembly, so it needs no client,
/// no world, and no second process. Tier 3 proves a packet arrives; this proves
/// the halves agree, and it costs nothing.
/// </summary>
public static class NetSweep
{
	/// <summary>
	/// A pair of halves to exercise, with the two instances they belong to.
	/// <para/>
	/// Two instances rather than one, because that is the situation being
	/// modelled: a server writes from its copy and a client reads into a
	/// different one. Reading back into the sender would hide a reader that
	/// happens to leave a field alone.
	/// </summary>
	public readonly record struct Subject(
		string Name,
		Action<BinaryWriter> WriteFromSender,
		Action<BinaryReader> ReadIntoReceiver,
		Action<BinaryWriter> WriteFromReceiver);

	/// <summary>Every <c>ModNPC</c> that sends extra AI, as <c>Mod/InternalName</c>.</summary>
	public static IEnumerable<string> Npcs()
		=> ContentSweep.Every<ModNPC>()
			.Where(qualified => ContentSweep.Overrides<ModNPC>(qualified,
				(nameof(ModNPC.SendExtraAI), [typeof(BinaryWriter)]),
				(nameof(ModNPC.ReceiveExtraAI), [typeof(BinaryReader)])))
			.Order();

	/// <summary>Every <c>ModProjectile</c> that sends extra AI.</summary>
	public static IEnumerable<string> Projectiles()
		=> ContentSweep.Every<ModProjectile>()
			.Where(qualified => ContentSweep.Overrides<ModProjectile>(qualified,
				(nameof(ModProjectile.SendExtraAI), [typeof(BinaryWriter)]),
				(nameof(ModProjectile.ReceiveExtraAI), [typeof(BinaryReader)])))
			.Order();

	/// <summary>Every <c>ModSystem</c> that syncs itself on a join.</summary>
	public static IEnumerable<string> Systems()
		=> ContentSweep.Every<ModSystem>()
			.Where(qualified => ContentSweep.Overrides<ModSystem>(qualified,
				(nameof(ModSystem.NetSend), [typeof(BinaryWriter)]),
				(nameof(ModSystem.NetReceive), [typeof(BinaryReader)])))
			.Order();

	/// <summary>
	/// Asserts that the writing half runs. The first rung.
	/// </summary>
	public static void WriteReads(Subject subject)
	{
		try {
			Written(subject);
		}
		catch (Exception bad) {
			Assert.Fail($"{subject.Name}'s send threw {Describe(bad)}, so nothing can be "
				+ "transmitted for it at all");
		}
	}

	/// <summary>
	/// Asserts that the reading half consumes exactly what the writing half
	/// produced. The second rung.
	/// <para/>
	/// Measured in bytes, not in calls, and that distinction is not pedantry. A
	/// mod in the corpus writes two <c>short</c>s and reads them back as one
	/// <c>Point16</c>: seventeen writes against sixteen reads, and entirely
	/// correct. A check that counted calls would report it. Only the stream
	/// position is the truth.
	/// </summary>
	public static void RoundTrips(Subject subject)
	{
		byte[] written;

		try {
			written = Written(subject);
		}
		catch (Exception bad) {
			Assert.Skip($"{subject.Name}'s send threw {Describe(bad)}, so there is nothing "
				+ "to read back.");
			return;
		}

		using MemoryStream stream = new(written, writable: false);
		using BinaryReader reader = new(stream);

		try {
			subject.ReadIntoReceiver(reader);
		}
		catch (Exception bad) {
			Assert.Fail($"{subject.Name} wrote {written.Length} "
				+ (written.Length == 1 ? "byte" : "bytes")
				+ $" and reading them back threw {Describe(bad)}. On a real client this "
				+ "is logged and swallowed, so the entity silently carries wrong state");
		}

		if (stream.Position == written.Length)
			return;

		long over = stream.Position - written.Length;

		Assert.Fail($"{subject.Name} wrote {written.Length} "
			+ (written.Length == 1 ? "byte" : "bytes")
			+ $" and read {stream.Position}, "
			+ (over > 0
				? $"so the reader wants {over} more than the writer sends"
				: $"so {-over} the writer sends are never read")
			+ ". The two halves disagree, and everything after them in the same "
			+ "stream is misaligned, including other mods' data");
	}

	/// <summary>
	/// Asserts that what the receiver would send on is what it was given. The
	/// third rung, and the one with something of its own to say.
	/// <para/>
	/// A reader that consumes the right number of bytes and puts them in the
	/// wrong fields, or drops one, passes the rung above: every byte is
	/// accounted for. What is gone is the state. Writing it out again from the
	/// receiver is the only thing that shows it, and the failure it catches is a
	/// client that disagrees with the server forever with nothing in any log.
	/// </summary>
	public static void Settles(Subject subject)
	{
		byte[] first;

		try {
			first = Written(subject);

			using MemoryStream stream = new(first, writable: false);
			using BinaryReader reader = new(stream);

			subject.ReadIntoReceiver(reader);
		}
		catch (Exception bad) {
			// Owned by the rungs above, which will report it. Saying so here as
			// well would turn one defect into two failures describing it
			// differently.
			Assert.Skip($"{subject.Name} could not be round tripped, which threw "
				+ $"{Describe(bad)}, so whether it settles has no answer.");
			return;
		}

		byte[] second;

		using (MemoryStream stream = new()) {
			using BinaryWriter writer = new(stream);

			try {
				subject.WriteFromReceiver(writer);
			}
			catch (Exception bad) {
				Assert.Fail($"{subject.Name} could be read into but then could not be "
					+ $"written out again, which threw {Describe(bad)}");
			}

			writer.Flush();
			second = stream.ToArray();
		}

		if (first.SequenceEqual(second))
			return;

		Assert.Fail($"{subject.Name} does not survive its own round trip. The receiver "
			+ "would send on something different from what it was given, so state is "
			+ "being lost or altered in the read with every byte accounted for:\n"
			+ $"  sent:     {Hex(first)}\n"
			+ $"  resent:   {Hex(second)}");
	}

	/// <summary>The NPC a swept name refers to, as a subject.</summary>
	/// <remarks>
	/// Two throwaway NPCs, never the <c>ContentSamples</c> instance.
	/// <c>ReceiveExtraAI</c> writes into the entity, and the samples are the
	/// canonical copy of every NPC that all inspection code reads, so reading
	/// into one would corrupt shared state for the rest of the session.
	/// <c>SetDefaults</c> gives each entity its own <c>ModNPC</c>
	/// (<c>NPCLoader</c> assigns <c>GetNPC(npc.type).NewInstance(npc)</c>), so a
	/// locally made NPC is private and safe to write into.
	/// </remarks>
	public static Subject RequireNpc(string qualified)
	{
		int type = ContentSweep.Require<ModNPC>(qualified).Type;

		ModNPC sender = Fresh(type).ModNPC;
		ModNPC receiver = Fresh(type).ModNPC;

		return new Subject(qualified, sender.SendExtraAI, receiver.ReceiveExtraAI, receiver.SendExtraAI);

		static NPC Fresh(int of)
		{
			NPC npc = new();
			npc.SetDefaults(of);

			return npc;
		}
	}

	/// <summary>The projectile a swept name refers to, as a subject.</summary>
	public static Subject RequireProjectile(string qualified)
	{
		int type = ContentSweep.Require<ModProjectile>(qualified).Type;

		ModProjectile sender = Fresh(type).ModProjectile;
		ModProjectile receiver = Fresh(type).ModProjectile;

		return new Subject(qualified, sender.SendExtraAI, receiver.ReceiveExtraAI, receiver.SendExtraAI);

		static Projectile Fresh(int of)
		{
			Projectile projectile = new();
			projectile.SetDefaults(of);

			return projectile;
		}
	}

	/// <summary>
	/// The system a swept name refers to, as a subject.
	/// <para/>
	/// One instance, because a <c>ModSystem</c> is a singleton and there is no
	/// second copy to read into. So this reads the live system's own message back
	/// into itself, which restores what it just sent when the halves agree and
	/// moves its state when they do not. That is the same hazard
	/// <see cref="PersistenceSweep"/> carries and it is unavoidable for the same
	/// reason: the state is global, and a box isolates a region rather than a
	/// flag.
	/// </summary>
	public static Subject RequireSystem(string qualified)
	{
		ModSystem system = ContentSweep.Require<ModSystem>(qualified);

		return new Subject(qualified, system.NetSend, system.NetReceive, system.NetSend);
	}

	/// <summary>What the sending half produces, as bytes.</summary>
	private static byte[] Written(Subject subject)
	{
		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream);

		subject.WriteFromSender(writer);
		writer.Flush();

		return stream.ToArray();
	}

	/// <summary>
	/// Bytes as hex, truncated. A payload is usually a handful of bytes and the
	/// difference between two of them is the whole message.
	/// </summary>
	private static string Hex(byte[] bytes)
	{
		const int Limit = 32;

		string body = Convert.ToHexString(bytes.Length <= Limit ? bytes : bytes[..Limit]);

		return bytes.Length == 0
			? "(nothing)"
			: bytes.Length <= Limit ? body : $"{body}... ({bytes.Length} bytes)";
	}

	private static string Describe(Exception thrown)
		=> $"{thrown.GetType().Name}: {thrown.Message}";

}
