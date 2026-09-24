// Same reason as the core's test project: this project references
// Testaria.Core, so an unqualified `Assert` inside namespace
// Testaria.Analyzers.Tests binds to Testaria.Assert through the enclosing
// namespace rather than to xUnit's. Alias xUnit's to a name that cannot
// collide and use the alias everywhere.
global using Xunit;
global using XAssert = Xunit.Assert;
