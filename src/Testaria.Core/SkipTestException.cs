namespace Testaria;

/// <summary>
/// Thrown to abandon a test as skipped rather than failed.
/// <para/>
/// Some reasons to skip are only knowable once the game is running: an
/// optional mod is not installed, a world lacks a biome the test needs, a
/// boss has not been defeated. A compile-time <c>Skip</c> attribute cannot
/// express any of those, and the alternatives are both bad. Failing says the
/// subject is broken when it is not; passing vacuously is worse still, because
/// it reports coverage that never happened.
/// </summary>
public sealed class SkipTestException(string reason) : Exception(reason);
