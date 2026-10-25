using System.Runtime.CompilerServices;
using NimbleWrite.Desktop.Core;

namespace NimbleWrite.Desktop.Tests;

internal static class TestSetup
{
    /// <summary>Runs once when the test assembly loads: tests must never write to the user's real NimbleWrite log.</summary>
#pragma warning disable CA2255 // a test assembly is exactly where a module initializer belongs
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void MuteAppLog() => Log.Muted = true;
}
