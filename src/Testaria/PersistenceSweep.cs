using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace Testaria;

/// <summary>
/// What a mod writes into a world or a player file, and whether it can read its
/// own writing back.
/// <para/>
/// The stakes are higher here than anywhere else in the sweep, because the
/// failure is not a crash during play. <c>WorldIO.LoadModData</c> wraps a throw
/// from <c>LoadWorldData</c> in a <c>CustomModDataException</c> that propagates
/// out of the world load, so the world stops opening; <c>PlayerIO</c> does the
/// same for <c>LoadData</c> and the player file stops opening. tModLoader's own
/// documentation states the requirement and nothing enforces it:
/// <i>"Try to write defensive loading code that won't crash if something's
/// missing."</i>
/// <para/>
/// Four questions, and they are deliberately separate results. Whether saving
/// works, whether a save can be read back, whether reading it back and saving
/// again gives the same bytes, and whether loading tolerates a tag that is
/// missing things. The third is the one that catches a loader which silently
/// drops a field: nothing throws, the tag is fully read, and the state is gone.
/// <para/>
/// <b>These checks call into live content.</b> Every one of them is written to
/// put back what it found: the tag saved at the start is loaded again at the
/// end, so a mod whose load really is the inverse of its save is left exactly as
/// it was. A mod for which that is untrue cannot be perfectly restored, and
/// that is the same defect the settling check reports, so a failure here is also
/// a warning that this subject's state may have moved.
/// </summary>
public static class PersistenceSweep
{
	/// <summary>
	/// Every <c>ModSystem</c> that actually persists something, as
	/// <c>Mod/InternalName</c>.
	/// <para/>
	/// Filtered to those overriding the hooks, because every mod has systems and
	/// almost none of them save anything. A subject that inherits both empty
	/// virtuals would pass all four checks without exercising a line of the
	/// mod's code, which is a result that means nothing and takes up a row.
	/// </summary>
	public static IEnumerable<string> Systems()
		=> ContentSweep.Every<ModSystem>()
			.Where(qualified => ContentSweep.Overrides<ModSystem>(qualified,
				(nameof(ModSystem.SaveWorldData), [typeof(TagCompound)]),
				(nameof(ModSystem.LoadWorldData), [typeof(TagCompound)])))
			.Order();

	/// <summary>
	/// Every <c>ModPlayer</c> that persists something.
	/// <para/>
	/// tModLoader already requires these two to be overridden together
	/// (<c>ModPlayer.ValidateType</c> calls <c>MustOverrideTogether</c>), so one
	/// of them being present means both are.
	/// </summary>
	public static IEnumerable<string> Players()
		=> ContentSweep.Every<ModPlayer>()
			.Where(qualified => ContentSweep.Overrides<ModPlayer>(qualified,
				(nameof(ModPlayer.SaveData), [typeof(TagCompound)]),
				(nameof(ModPlayer.LoadData), [typeof(TagCompound)])))
			.Order();

	/// <summary>Asserts that saving does not throw. The first rung.</summary>
	/// <remarks>
	/// A throw here is not a crash: <c>WorldIO.SaveModData</c> catches it and
	/// discards the mod's entire world data with
	/// <c>saveData = new TagCompound()</c>, on the reasoning that half-broken
	/// data compounds errors. So this rung is about silent data loss, which is
	/// worse to diagnose than a crash and easier to miss.
	/// </remarks>
	public static void SaveReads(Subject subject)
	{
		TagCompound tag = new();

		try {
			subject.Save(tag);
		}
		catch (Exception bad) {
			Assert.Fail($"{subject.Name}'s save threw {Describe(bad)}. tModLoader catches "
				+ "this and discards the mod's whole record rather than crashing, so the "
				+ "data is simply gone at the next save");
		}
	}

	/// <summary>
	/// Asserts that a save can be read back. The second rung.
	/// </summary>
	public static void RoundTrips(Subject subject)
		=> Restoring(subject, saved => {
			try {
				subject.Load(Copy(saved));
			}
			catch (Exception bad) {
				Assert.Fail($"{subject.Name} cannot read back what it just saved, which "
					+ $"threw {Describe(bad)}. In a real load this becomes a "
					+ "CustomModDataException and the file stops opening");
			}
		});

	/// <summary>
	/// Asserts that saving, loading, and saving again gives the same tag. The
	/// third rung, and the one with something of its own to say.
	/// <para/>
	/// A loader that quietly drops a field passes the rung above: nothing
	/// throws, every key is read, and the tag is consumed. What is gone is the
	/// state, and the only way to see it is to write the state out a second time
	/// and find it changed. In a world that is a value which decays a little on
	/// every save.
	/// </summary>
	public static void Settles(Subject subject)
		=> Restoring(subject, saved => {
			TagCompound again = new();

			try {
				subject.Load(Copy(saved));
				subject.Save(again);
			}
			catch (Exception bad) {
				// The rung above owns this question and will report it. Saying
				// so twice would make one defect arrive as two failures.
				Assert.Skip($"{subject.Name} could not be round tripped, which threw "
					+ $"{Describe(bad)}, so whether it settles has no answer.");
				return;
			}

			if (Same(saved, again))
				return;

			Assert.Fail($"{subject.Name} does not survive its own round trip. Saving, "
				+ "loading that back, and saving again produced different data, so the "
				+ "stored state changes every time it is written:\n"
				+ $"  first:  {Show(saved)}\n"
				+ $"  second: {Show(again)}");
		});

	/// <summary>
	/// Asserts that loading tolerates a tag with nothing in it.
	/// <para/>
	/// Not a rung of the round trip: it asks whether the inbound half copes with
	/// input the outbound half would never produce. That is the version-skew
	/// case, and it is the common one. A world saved by an older build of the
	/// mod holds a tag missing whatever the new build reads, and
	/// <c>TagCompound.Get</c> answers a missing key with a default rather than
	/// throwing, so what breaks is the code after the read: an index, a
	/// <c>First()</c>, a parse, an assumption that something is not null.
	/// <para/>
	/// Destructive, and handled the same way as the rest: the real tag is saved
	/// first and loaded again afterwards.
	/// </summary>
	public static void LoadsAbsentKeys(Subject subject)
		=> Restoring(subject, _ => {
			try {
				subject.Load(new TagCompound());
			}
			catch (Exception bad) {
				Assert.Fail($"{subject.Name} cannot load a tag with nothing in it, which "
					+ $"threw {Describe(bad)}. That is what a save written by an older "
					+ "build of this mod looks like, and in a real load it becomes a "
					+ "CustomModDataException that stops the file opening");
			}
		});

	/// <summary>
	/// Asserts that loading survives a record holding more than this build would
	/// write.
	/// <para/>
	/// Not a rung of the round trip, and not the same question as the empty tag.
	/// A missing key is answered by a default; a key holding <i>more</i> than
	/// expected is answered by whatever the mod does next, and what it often does
	/// is copy into something fixed. The corpus has the case this exists for:
	/// CheatSheet's <c>LoadData</c> copies the saved accessory list into an array
	/// sized by a mutable public static, with <c>List.CopyTo</c>, which throws
	/// <c>ArgumentException</c> when the source is longer than the destination.
	/// An empty tag cannot reach it, because copying nothing always fits, and a
	/// round trip cannot reach it, because both sides use today's size.
	/// <para/>
	/// The mutation is the cheapest available stand-in for "a save from a build
	/// that stored more": take the tag the mod itself produced and duplicate the
	/// entries of every list in it, at every depth. It invents no values, so a
	/// loader that reads the list is handed exactly the kind of thing it already
	/// knows how to read, only more of it.
	/// <para/>
	/// A skip when there is nothing to lengthen. A mod storing no lists cannot
	/// fail this, and reporting a pass would claim a check that never ran.
	/// </summary>
	public static void SurvivesALongerRecord(Subject subject)
		=> Restoring(subject, saved => {
			TagCompound stretched = Copy(saved);

			if (Lengthen(stretched) == 0) {
				Assert.Skip($"{subject.Name} stores no lists, so there is nothing to make "
					+ "longer than this build would write.");
			}

			try {
				subject.Load(stretched);
			}
			catch (Exception bad) {
				Assert.Fail($"{subject.Name} cannot load a record holding more entries than "
					+ $"this build writes, which threw {Describe(bad)}. That is what a save "
					+ "from a build with a larger limit looks like, and in a real load it "
					+ "becomes a CustomModDataException that stops the file opening");
			}
		});

	/// <summary>
	/// Doubles every list in a tag, in place, at every depth, and says how many
	/// it changed.
	/// <para/>
	/// Entries are repeated rather than invented. A list of tag compounds stays a
	/// list of tag compounds the loader can read, so a failure is about the
	/// count and not about a value the mod has never seen.
	/// </summary>
	private static int Lengthen(TagCompound tag)
	{
		int changed = 0;

		// Keys are collected first: the loop assigns back into the tag, and
		// mutating a dictionary while enumerating it throws.
		foreach (string key in tag.Select(entry => entry.Key).ToList()) {
			if (!tag.TryGet<object>(key, out object? value))
				continue;

			switch (value) {
				case TagCompound nested:
					changed += Lengthen(nested);
					break;

				case System.Collections.IList list when list.Count > 0:
					foreach (object? element in list.Cast<object?>().ToList()) {
						if (element is TagCompound inner)
							changed += Lengthen(inner);

						list.Add(element);
					}

					changed++;
					break;
			}
		}

		return changed;
	}

	/// <summary>
	/// One thing that persists, with its two hooks, whichever kind it is.
	/// <para/>
	/// A world system and a player both save into a <c>TagCompound</c> and read
	/// one back, so every check above is written once against this rather than
	/// twice against the two kinds.
	/// </summary>
	public readonly record struct Subject(string Name, Action<TagCompound> Save, Action<TagCompound> Load);

	/// <summary>
	/// The system a swept name refers to, as a subject.
	/// <para/>
	/// A skip when no world is loaded. World data is state about a world, and a
	/// mod reading <c>Main.tile</c> or a world array to save it would throw
	/// here for a reason that has nothing to do with the mod. In a normal run
	/// this never fires, because the session only starts once the world is up
	/// (<c>TestariaSystem</c> decides the run's ceiling from
	/// <c>Main.maxTilesX</c>), so tier 1 tests execute with a world present even
	/// though they do not require one.
	/// </summary>
	public static Subject RequireSystem(string qualified)
	{
		ModSystem system = ContentSweep.Require<ModSystem>(qualified);

		if (Main.maxTilesX <= 0) {
			Assert.Skip($"no world is loaded, so {qualified}'s world data is about "
				+ "nothing and saving it would be asking the wrong question.");
		}

		return new Subject(qualified, system.SaveWorldData, system.LoadWorldData);
	}

	/// <summary>
	/// The player hooks a swept name refers to, as a subject.
	/// <para/>
	/// Bound to a throwaway <c>Player</c> rather than to anybody's real one. A
	/// <c>ModPlayer</c> is per-player state, and running a load against a player
	/// in the world would rewrite that player; a fresh one belongs to nothing.
	/// <para/>
	/// A skip when a bare <c>Player</c> cannot be made or does not carry the
	/// mod's own <c>ModPlayer</c>. That is a fact about what a headless process
	/// will construct, not about the mod.
	/// </summary>
	public static Subject RequirePlayer(string qualified)
	{
		ModPlayer template = ContentSweep.Require<ModPlayer>(qualified);
		Player carrier;

		try {
			carrier = new Player();
		}
		catch (Exception bad) {
			Assert.Skip($"a bare Player could not be constructed here, which threw "
				+ $"{Describe(bad)}, so {qualified} has nothing to be attached to.");
			throw;
		}

		ModPlayer instance;

		try {
			instance = carrier.GetModPlayer(template);
		}
		catch (Exception bad) {
			Assert.Skip($"{qualified} could not be reached on a bare Player, which threw "
				+ $"{Describe(bad)}.");
			throw;
		}

		return new Subject(qualified, instance.SaveData, instance.LoadData);
	}

	/// <summary>
	/// Runs a check with the subject's current state captured first and put back
	/// afterwards.
	/// <para/>
	/// The capture is also what most of the checks are about, so it is passed in
	/// rather than merely held. Restoration runs even when the check fails,
	/// because a failing subject is exactly the one most likely to have been
	/// left somewhere odd.
	/// <para/>
	/// A save that throws leaves nothing to restore from, and there is no
	/// question left to ask either: every check here starts from a tag the
	/// subject produced. That is the first rung's business, so this skips and
	/// lets it report.
	/// </summary>
	private static void Restoring(Subject subject, Action<TagCompound> check)
	{
		TagCompound saved = new();

		try {
			subject.Save(saved);
		}
		catch (Exception bad) {
			Assert.Skip($"{subject.Name}'s save threw {Describe(bad)}, so there is no "
				+ "record to read back.");
			return;
		}

		try {
			check(saved);
		}
		finally {
			try {
				subject.Load(Copy(saved));
			}
			catch {
				// Nothing useful to do with this. Restoration failing means the
				// load is not the inverse of the save, which the settling check
				// reports as the finding it is; throwing from a finally here
				// would replace that report with this one.
			}
		}
	}

	/// <summary>
	/// A tag that can be handed to a loader without the original being at risk.
	/// <para/>
	/// A load is free to take a reference to a list it was given, or to mutate
	/// one in place, and the original is needed again afterwards to restore
	/// from. Cloning through tModLoader's own serializer is the only copy that
	/// is guaranteed to mean the same thing as the tag it came from.
	/// </summary>
	private static TagCompound Copy(TagCompound tag) => TagIO.Clone(tag);

	/// <summary>
	/// Whether two tags hold the same thing.
	/// <para/>
	/// Structural rather than textual, and order-insensitive between keys: a
	/// <c>TagCompound</c> is a dictionary, and two saves that write the same
	/// pairs in a different order have saved the same thing. Lists are ordered
	/// and compared as such.
	/// </summary>
	private static bool Same(object? left, object? right)
	{
		if (left is null || right is null)
			return left is null && right is null;

		if (left is TagCompound a && right is TagCompound b) {
			if (a.Count != b.Count)
				return false;

			foreach (KeyValuePair<string, object> entry in a) {
				// TryGet rather than an indexer lookup: TagCompound answers a
				// missing key with a default rather than saying it was missing,
				// so asking for one that is absent would compare a default
				// against a real value and call them different for the wrong
				// reason.
				if (!b.TryGet(entry.Key, out object? other) || !Same(entry.Value, other))
					return false;
			}

			return true;
		}

		if (left is System.Collections.IList first && right is System.Collections.IList second) {
			if (first.Count != second.Count)
				return false;

			for (int at = 0; at < first.Count; at++) {
				if (!Same(first[at], second[at]))
					return false;
			}

			return true;
		}

		return left.Equals(right);
	}

	/// <summary>A tag as text, truncated so one large record does not bury the failure.</summary>
	private static string Show(TagCompound tag)
	{
		const int Limit = 400;

		string printed = TagPrinter.Print(tag).ReplaceLineEndings(" ");

		return printed.Length <= Limit ? printed : printed[..Limit] + " ...";
	}

	private static string Describe(Exception thrown)
		=> $"{thrown.GetType().Name}: {thrown.Message}";

}
