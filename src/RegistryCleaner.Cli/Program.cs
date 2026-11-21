using System.CommandLine;
using RegistryCleaner.Core.Safety;

namespace RegistryCleaner.Cli;

/// <summary>
/// Entry point for the <c>rc</c> command-line interface.
/// </summary>
/// <remarks>
/// The full command surface — <c>scan</c>, <c>plan</c>, <c>apply</c>, <c>restore</c>,
/// <c>backups</c>, <c>verify</c>, <c>rules</c>, <c>export</c> — is specified in
/// <c>docs/UI.md</c> and arrives in milestone M1. M0 establishes the entry point, the
/// elevation probe, and the help text.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        var rootCommand = new RootCommand(
            """
            Registry Cleaner - find, review, and remove stale Windows registry entries.

            Every finding is ranked by severity and carries evidence. Nothing is removed
            without an explicit selection, and every removal is backed by a verified
            backup that can be restored.
            """);

        rootCommand.SetAction(_ =>
        {
            Console.WriteLine($"Registry Cleaner {version}");
            Console.WriteLine($"Elevated: {(ElevationGuard.IsElevated() ? "yes" : "no")}");
            Console.WriteLine();
            Console.WriteLine("Milestone M0: the command surface arrives in M1.");
            Console.WriteLine("See docs/UI.md for the planned command reference.");
            return 0;
        });

        return rootCommand.Parse(args).Invoke();
    }
}
