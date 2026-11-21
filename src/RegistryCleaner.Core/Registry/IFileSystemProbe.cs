namespace RegistryCleaner.Core.Registry;

/// <summary>
/// The outcome of checking whether a filesystem path exists.
/// </summary>
/// <remarks>
/// <see cref="Unverifiable"/> is a first-class outcome, not a flavour of
/// <see cref="Missing"/>. A network share that is currently disconnected, a removable
/// drive that is not inserted, and a path that cannot be parsed are all
/// <em>unverifiable</em> — claiming those files are "missing" is the single largest
/// source of false positives in registry cleaners (see <c>docs/HOW-IT-WORKS.md</c> §3).
/// </remarks>
public enum FileProbeResult
{
    /// <summary>The path was probed and does exist.</summary>
    Exists,

    /// <summary>The path was probed and does not exist.</summary>
    Missing,

    /// <summary>
    /// The path could not be resolved to a definitive answer — unreachable network
    /// location, absent removable media, unparsable path, or an access failure.
    /// </summary>
    Unverifiable,
}

/// <summary>
/// Probes the filesystem for the existence of paths referenced by registry values.
/// </summary>
/// <remarks>
/// This is an interface so that rules can be tested without touching a real disk
/// (ADR-017).
/// </remarks>
public interface IFileSystemProbe
{
    /// <summary>
    /// Determines whether <paramref name="path"/> exists.
    /// </summary>
    /// <remarks>
    /// Implementations must never report <see cref="FileProbeResult.Missing"/> for a path
    /// they could not actually reach.
    /// </remarks>
    FileProbeResult Probe(string path);

    /// <summary>
    /// Expands environment variables such as <c>%ProgramFiles%</c> in
    /// <paramref name="rawPath"/>.
    /// </summary>
    /// <returns>
    /// The expanded path, or <see langword="null"/> if the path could not be expanded.
    /// </returns>
    string? ResolveEnvironment(string rawPath);
}
