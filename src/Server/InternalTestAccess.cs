using System.Runtime.CompilerServices;

// Keep implementation helpers internal to the Server assembly while allowing
// the dedicated unit-test assembly to verify their security-sensitive behavior.
[assembly: InternalsVisibleTo("GameNet.Server.UnitTests")]
