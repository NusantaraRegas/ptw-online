using System.Runtime.CompilerServices;

// The render retry schedule is an internal policy detail; the print regression suite asserts it directly
// so backoff and the attempt cap stay covered without hosting the Worker.
[assembly: InternalsVisibleTo("Ptw.Printing.Tests")]
