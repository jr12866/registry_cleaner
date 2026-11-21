using RegistryCleaner.Core.Findings;
using RegistryCleaner.Core.Registry;
using RegistryCleaner.Core.Safety.PathNormalization;

namespace RegistryCleaner.Core.Safety;

/// <summary>
/// Decides whether a registry target may be removed.
/// </summary>
/// <remarks>
/// <para>
/// This is the last line of defence between the rule engine and the registry. It is
/// enforced in code, and rules have no way to opt out of it (ADR-008). Two things follow
/// from that: the guard may only ever <em>raise</em> a risk tier, never lower one, and
/// it is consulted again at apply time, not only at scan time.
/// </para>
/// <para>
/// See <c>docs/SAFETY.md</c> §4 for the protected path list and the reasoning.
/// </para>
/// </remarks>
public sealed class ProtectedPathGuard
{
    private readonly Dictionary<string, IReadOnlyList<ProtectedPath>> _protectedKeys;
    private readonly IReadOnlySet<string> _protectedValueNames;
    private readonly IReadOnlyList<string> _protectedFilePaths;
    private readonly IPathAliasResolver _aliasResolver;

    /// <summary>
    /// Creates a guard.
    /// </summary>
    /// <param name="paths">
    /// The protected registry paths. Defaults to <see cref="ProtectedPaths.Default"/>.
    /// </param>
    /// <param name="protectedValueNames">
    /// Value names that are never removable. Defaults to
    /// <see cref="ProtectedPaths.ProtectedValueNames"/>.
    /// </param>
    /// <param name="protectedFilePaths">
    /// Protected file system locations. Defaults to
    /// <see cref="ProtectedPaths.ProtectedFilePaths"/>.
    /// </param>
    /// <param name="aliasResolver">
    /// Resolves 8.3 short names. Defaults to
    /// <see cref="NullPathAliasResolver"/>.
    /// </param>
    public ProtectedPathGuard(
        IEnumerable<ProtectedPath>? paths = null,
        IReadOnlySet<string>? protectedValueNames = null,
        IEnumerable<string>? protectedFilePaths = null,
        IPathAliasResolver? aliasResolver = null)
    {
        _protectedKeys = new Dictionary<string, IReadOnlyList<ProtectedPath>>(StringComparer.OrdinalIgnoreCase);
        _protectedValueNames = protectedValueNames ?? ProtectedPaths.ProtectedValueNames;

        // The alias resolver must be assigned before the protected file paths are
        // normalised, because normalisation resolves short names through it.
        _aliasResolver = aliasResolver ?? NullPathAliasResolver.Instance;

        _protectedFilePaths = (protectedFilePaths ?? ProtectedPaths.ProtectedFilePaths)
            .Select(NormalizeFilePath)
            .ToArray();

        foreach (var path in paths ?? ProtectedPaths.Default)
        {
            foreach (var canonicalKey in ExpandToCanonicalKeys(path))
            {
                if (_protectedKeys.TryGetValue(canonicalKey, out var existing))
                {
                    _protectedKeys[canonicalKey] = existing.Concat([path]).ToArray();
                }
                else
                {
                    _protectedKeys[canonicalKey] = [path];
                }
            }
        }
    }

    /// <summary>
    /// Returns whether the target is protected, and why.
    /// </summary>
    /// <param name="location">The target to test.</param>
    /// <returns>The reason, or <see langword="null"/> when the target is allowed.</returns>
    public string? Explain(RegistryLocation location)
    {
        // A protected value name is never removable, whatever key it sits in.
        if (location.ValueName is not null &&
            _protectedValueNames.Contains(location.ValueName))
        {
            return $"Value name '{location.ValueName}' is on the protected value-name list.";
        }

        foreach (var candidateKey in CandidateKeys(location))
        {
            foreach (var ancestor in RegistryPathNormalizer.Ancestors(candidateKey))
            {
                if (!_protectedKeys.TryGetValue(ancestor, out var entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (entry.IsValueScoped)
                    {
                        // A value-scoped entry only protects that one value.
                        if (location.ValueName is not null &&
                            string.Equals(location.ValueName, entry.ValueName, StringComparison.OrdinalIgnoreCase))
                        {
                            return $"{entry.Reason} (protected value '{entry.ValueName}').";
                        }

                        continue;
                    }

                    // A subtree-scoped entry protects the key and everything beneath it.
                    return entry.Reason;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns whether the target is protected.
    /// </summary>
    public bool IsProtected(RegistryLocation location) => Explain(location) is not null;

    /// <summary>
    /// Evaluates a candidate at the proposed risk tier.
    /// </summary>
    /// <remarks>
    /// A protected target is raised to <see cref="RiskTier.Critical"/> and becomes
    /// non-selectable. The guard never lowers a tier.
    /// </remarks>
    public GuardResult Evaluate(RegistryLocation location, RiskTier proposedRisk)
    {
        var reason = Explain(location);
        return reason is null ? GuardResult.Allowed(proposedRisk) : GuardResult.Blocked(reason);
    }

    /// <summary>
    /// Evaluates a finding, returning it unchanged when it is allowed, or with its risk
    /// raised and selection disabled when it is protected.
    /// </summary>
    public Finding Evaluate(Finding finding)
    {
        var result = Evaluate(finding.Location, finding.Risk);
        return result.IsProtected
            ? finding.WithRisk(result.EffectiveRisk, selectable: false)
            : finding.WithRisk(result.EffectiveRisk, result.Selectable);
    }

    /// <summary>
    /// Returns whether a file path points at a protected file system location.
    /// </summary>
    /// <remarks>
    /// The path is environment-expanded and 8.3-short-name-resolved before comparison, so
    /// that <c>C:\PROGRA~1\App</c> and <c>C:\Program Files\App</c> are recognised as the
    /// same location.
    /// </remarks>
    public bool IsProtectedFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var candidate = NormalizeFilePath(path);

        foreach (var protectedPath in _protectedFilePaths)
        {
            if (RegistryPathNormalizer.IsUnder(candidate, protectedPath))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Produces the canonical keys a location can correspond to.
    /// </summary>
    /// <remarks>
    /// <c>HKEY_CLASSES_ROOT</c> is a merged view over <c>HKLM\SOFTWARE\Classes</c> and
    /// <c>HKCU\Software\Classes</c>, so a location under it may resolve to either. Both
    /// are checked.
    /// </remarks>
    private static IEnumerable<string> CandidateKeys(RegistryLocation location)
    {
        var normalizedPath = RegistryPathNormalizer.Normalize(location.KeyPath);

        if (location.Hive == RegistryHive.HKEY_CLASSES_ROOT)
        {
            yield return $"HKEY_LOCAL_MACHINE\\SOFTWARE\\CLASSES\\{normalizedPath}";
            yield return $"HKEY_CURRENT_USER\\SOFTWARE\\CLASSES\\{normalizedPath}";
            yield break;
        }

        yield return $"{location.Hive}\\{normalizedPath}";
    }

    /// <summary>
    /// Produces the canonical keys a protected path corresponds to, expanding
    /// <c>HKEY_CLASSES_ROOT</c> over its two backing locations.
    /// </summary>
    private static IEnumerable<string> ExpandToCanonicalKeys(ProtectedPath path)
    {
        var normalizedPath = RegistryPathNormalizer.Normalize(path.KeyPath);

        if (path.Hive == RegistryHive.HKEY_CLASSES_ROOT)
        {
            yield return $"HKEY_LOCAL_MACHINE\\SOFTWARE\\CLASSES\\{normalizedPath}";
            yield return $"HKEY_CURRENT_USER\\SOFTWARE\\CLASSES\\{normalizedPath}";
            yield break;
        }

        yield return $"{path.Hive}\\{normalizedPath}";
    }

    /// <summary>
    /// Environment-expands, alias-resolves, and canonicalises a file path for comparison.
    /// </summary>
    private string NormalizeFilePath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        var resolved = _aliasResolver.Resolve(expanded);
        return RegistryPathNormalizer.Normalize(resolved);
    }
}
