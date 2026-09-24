// The tool references Testaria.Core, so an unqualified `Assert` inside
// namespace Testaria.Tool.Tests binds to Testaria.Assert through the
// enclosing namespace rather than to xUnit's. Most of the method names exist
// on both, so the mistake compiles and silently tests with the wrong library.
// Alias xUnit's and use the alias everywhere.
global using Xunit;
global using XAssert = Xunit.Assert;
