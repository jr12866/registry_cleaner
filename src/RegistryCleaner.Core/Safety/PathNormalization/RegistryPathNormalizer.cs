namespace RegistryCleaner.Core.Safety.PathNormalization;

/// <summary>
/// Canonicalises registry key paths so that they can be compared reliably.
/// </summary>
/// <remarks>
/// <para>
/// Two registry paths that name the same key can look different: separators may be
/// <c>/</c> or <c>\</c>, a path may carry a trailing separator, and a path may contain
/// <c>.</c> or <c>..</c> segments. Any of those differences would let a rule slip a
/// protected path past a naive string comparison, so every comparison in the guard goes
/// through here first.
/// </para>
/// <para>
/// Registry key names are compared case-insensitively, so the canonical form is
/// upper-cased. The canonical form is for comparison only — display code should use the
/// original path.
/// </para>
/// </remarks>
public static class RegistryPathNormalizer
{
    /// <summary>
    /// Returns the canonical, upper-cased, comparison form of a registry key path.
    /// </summary>
    /// <returns>
    /// The canonical path, or an empty string when <paramref name="path"/> is null or blank.
    /// </returns>
    public static string Normalize(string? path)
    {
        var segments = SplitSegments(path);
        return segments.Count == 0 ? string.Empty : string.Join('\\', segments);
    }

    /// <summary>
    /// Splits a registry key path into its resolved, upper-cased segments.
    /// </summary>
    /// <remarks>
    /// Empty segments and <c>.</c> segments are dropped. A <c>..</c> segment removes the
    /// preceding segment; it never escapes above the root.
    /// </remarks>
    public static IReadOnlyList<string> SplitSegments(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Array.Empty<string>();
        }

        var normalized = path.Replace('/', '\\').Trim();
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            if (part is ".")
            {
                continue;
            }

            if (part is "..")
            {
                if (result.Count > 0)
                {
                    result.RemoveAt(result.Count - 1);
                }

                continue;
            }

            result.Add(part.ToUpperInvariant());
        }

        return result;
    }

    /// <summary>
    /// Returns whether <paramref name="candidate"/> is <paramref name="ancestor"/> or
    /// lies beneath it.
    /// </summary>
    /// <remarks>
    /// This is the "protected subtree" test: protecting
    /// <c>HKLM\SYSTEM\CurrentControlSet\Control\Class</c> also protects everything under it.
    /// </remarks>
    public static bool IsUnder(string? candidate, string? ancestor)
    {
        var candidateSegments = SplitSegments(candidate);
        var ancestorSegments = SplitSegments(ancestor);

        if (ancestorSegments.Count == 0 || candidateSegments.Count < ancestorSegments.Count)
        {
            return false;
        }

        for (var i = 0; i < ancestorSegments.Count; i++)
        {
            if (!string.Equals(candidateSegments[i], ancestorSegments[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns every ancestor of a canonical path, from the hive down to the path itself.
    /// </summary>
    public static IReadOnlyList<string> Ancestors(string? canonicalPath)
    {
        var segments = SplitSegments(canonicalPath);
        var result = new List<string>(segments.Count);

        for (var i = 1; i <= segments.Count; i++)
        {
            result.Add(string.Join('\\', segments.Take(i)));
        }

        return result;
    }
}
