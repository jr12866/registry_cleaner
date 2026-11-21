using RegistryCleaner.Core.Registry;

namespace RegistryCleaner.TestHarness;

/// <summary>
/// A fake filesystem for tests: it reports which paths exist, which are missing, and
/// which cannot be verified.
/// </summary>
/// <remarks>
/// The third category is the important one. A path on an unreachable network share is
/// <em>unverifiable</em>, not missing — conflating the two is the single largest source
/// of false positives in registry cleaners (see <c>docs/HOW-IT-WORKS.md</c> §3).
/// </remarks>
public sealed class FakeFileSystemProbe : IFileSystemProbe
{
    private readonly HashSet<string> _existing = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unverifiable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a probe with no known paths.
    /// </summary>
    public FakeFileSystemProbe()
    {
    }

    /// <summary>
    /// Creates a probe where the given paths exist.
    /// </summary>
    public FakeFileSystemProbe WithExisting(params string[] paths)
    {
        foreach (var path in paths)
        {
            _existing.Add(path);
        }

        return this;
    }

    /// <summary>
    /// Creates a probe where the given paths cannot be verified.
    /// </summary>
    public FakeFileSystemProbe WithUnverifiable(params string[] paths)
    {
        foreach (var path in paths)
        {
            _unverifiable.Add(path);
        }

        return this;
    }

    /// <summary>
    /// The paths this probe reports as existing.
    /// </summary>
    public IReadOnlyCollection<string> ExistingPaths => _existing;

    /// <inheritdoc />
    public FileProbeResult Probe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return FileProbeResult.Unverifiable;
        }

        if (_unverifiable.Contains(path))
        {
            return FileProbeResult.Unverifiable;
        }

        return _existing.Contains(path) ? FileProbeResult.Exists : FileProbeResult.Missing;
    }

    /// <inheritdoc />
    public string? ResolveEnvironment(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(rawPath);

        // ExpandEnvironmentVariables leaves an unknown variable untouched, so an
        // unresolved "%FOO%" means the path could not be expanded.
        if (rawPath.Contains('%') && expanded.Contains('%'))
        {
            return null;
        }

        return expanded;
    }
}
