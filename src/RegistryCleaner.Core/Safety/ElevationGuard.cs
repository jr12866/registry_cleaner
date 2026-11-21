using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace RegistryCleaner.Core.Safety;

/// <summary>
/// Answers whether the current process is elevated, and can relaunch the tool
/// elevated through UAC.
/// </summary>
/// <remarks>
/// Registry writes under <c>HKLM</c> require administrator rights. The tool refuses to
/// apply without elevation rather than half-applying a change (see
/// <c>docs/SAFETY.md</c> §7).
/// </remarks>
public static class ElevationGuard
{
    /// <summary>
    /// Returns whether the current process holds the Administrators role.
    /// </summary>
    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Attempts to relaunch the current executable elevated via the <c>runas</c> verb.
    /// </summary>
    /// <param name="arguments">The command-line arguments to pass to the new process.</param>
    /// <returns>
    /// <see langword="true"/> if the elevated process was launched. <see langword="false"/>
    /// if the user declined the UAC prompt or the launch failed.
    /// </returns>
    /// <remarks>
    /// The elevated instance runs as a separate process; the caller should exit once
    /// this returns <see langword="true"/>.
    /// </remarks>
    public static bool TryRelaunchElevated(IReadOnlyList<string>? arguments = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return false;
        }

        var commandLine = BuildCommandLine(arguments);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = commandLine,
                Verb = "runas",
                UseShellExecute = true,
            };

            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Win32Exception)
        {
            // The user declined the UAC prompt, or the launch was blocked.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string BuildCommandLine(IReadOnlyList<string>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(' ', arguments.Select(Quote));
    }

    private static string Quote(string argument) =>
        argument.Contains(' ', StringComparison.Ordinal)
            ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
}
