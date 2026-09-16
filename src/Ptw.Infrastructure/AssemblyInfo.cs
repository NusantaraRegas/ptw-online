using System.Runtime.CompilerServices;

// The print renderer and its controlled template content are internal to the infrastructure module.
// The document regression suite needs direct access to assert layout fidelity against the originals.
[assembly: InternalsVisibleTo("Ptw.Printing.Tests")]
