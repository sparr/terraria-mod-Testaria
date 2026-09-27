namespace Testaria.Tests;

/// <summary>
/// <see cref="SweepDeclarations"/>, the mirror of <see cref="SweepExemptions"/>:
/// for a check the sweep would run and cannot, because it cannot recognise the
/// subject.
/// </summary>
public class SweepDeclarationsTests : IDisposable
{
	// The registry is static, as it must be to be filled by another mod's load.
	// Each test clears it before and after so that ordering cannot matter.
	public SweepDeclarationsTests() => SweepDeclarations.Clear();

	public void Dispose() => SweepDeclarations.Clear();

	[Fact]
	public void A_declared_set_can_be_found_again()
	{
		int[] set = [-1, -1, 0];

		SweepDeclarations.DeclareRedirection("Mod/Set", set, none: -1, space: "NPC");

		RedirectionSet? found = SweepDeclarations.Redirection("Mod/Set");

		XAssert.NotNull(found);
		XAssert.Equal("NPC", found!.Value.Space);
		XAssert.Equal(-1, found.Value.None);
		XAssert.Same(set, found.Value.Set);
	}

	/// <summary>
	/// Held by reference, not copied. A set can be added to after it is declared,
	/// and the check should see what the game will see rather than what was true
	/// at load.
	/// </summary>
	[Fact]
	public void A_declared_set_is_held_by_reference()
	{
		int[] set = [-1, -1, -1];

		SweepDeclarations.DeclareRedirection("Mod/Set", set, none: -1, space: "NPC");
		set[2] = 0;

		XAssert.Equal(0, SweepDeclarations.Redirection("Mod/Set")!.Value.Set[2]);
	}

	[Fact]
	public void Nothing_declared_is_found_as_null()
		=> XAssert.Null(SweepDeclarations.Redirection("Mod/NeverDeclared"));

	[Fact]
	public void Declaring_the_same_name_twice_replaces_it()
	{
		SweepDeclarations.DeclareRedirection("Mod/Set", [-1], none: -1, space: "NPC");
		SweepDeclarations.DeclareRedirection("Mod/Set", [0, 0], none: 0, space: "Item");

		RedirectionSet found = SweepDeclarations.Redirection("Mod/Set")!.Value;

		XAssert.Equal("Item", found.Space);
		XAssert.Equal(2, found.Set.Length);
		XAssert.Single(SweepDeclarations.Redirections);
	}

	[Fact]
	public void Everything_declared_is_enumerable()
	{
		SweepDeclarations.DeclareRedirection("A/One", [-1], none: -1, space: "NPC");
		SweepDeclarations.DeclareRedirection("B/Two", [-1], none: -1, space: "Item");

		XAssert.Equal(["A/One", "B/Two"],
			SweepDeclarations.Redirections.Select(r => r.Name).Order());
	}

	/// <summary>
	/// Cleared on unload, and this is the test that matters most: a declaration
	/// holds a reference to an array inside a mod's assembly, so one surviving a
	/// reload would keep that assembly alive.
	/// </summary>
	[Fact]
	public void Clearing_forgets_everything()
	{
		SweepDeclarations.DeclareRedirection("Mod/Set", [-1], none: -1, space: "NPC");

		SweepDeclarations.Clear();

		XAssert.Empty(SweepDeclarations.Redirections);
		XAssert.Null(SweepDeclarations.Redirection("Mod/Set"));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void A_declaration_needs_a_name(string name)
		=> XAssert.Throws<ArgumentException>(()
			=> SweepDeclarations.DeclareRedirection(name, [-1], none: -1, space: "NPC"));

	[Fact]
	public void A_declaration_needs_a_space()
		=> XAssert.Throws<ArgumentException>(()
			=> SweepDeclarations.DeclareRedirection("Mod/Set", [-1], none: -1, space: " "));

	[Fact]
	public void A_declaration_needs_a_set()
		=> XAssert.Throws<ArgumentNullException>(()
			=> SweepDeclarations.DeclareRedirection("Mod/Set", null!, none: -1, space: "NPC"));
}
