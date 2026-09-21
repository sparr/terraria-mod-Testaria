// Every test file needs Xunit, and every test file that touches Testaria's
// own Assert needs both types visible at once. An unqualified `Assert` inside
// namespace Testaria.Tests binds to Testaria.Assert through the enclosing
// namespace, which would silently shadow xUnit's, so xUnit's is aliased to a
// name that cannot collide and the alias is used everywhere.
global using Xunit;
global using XAssert = Xunit.Assert;
