using RegistryCleaner.Core.Registry;
using RegistryCleaner.Core.Safety.PathNormalization;

namespace RegistryCleaner.TestHarness;

/// <summary>
/// An in-memory implementation of <see cref="IRegistryReader"/>, so that rules can be
/// tested against a known registry state without touching a real machine.
/// </summary>
/// <remarks>
/// <para>
/// Keys are stored per registry view. A key created with
/// <see cref="RegistryViewScope.Default"/> is visible from any requested view; a key
/// created with an explicit view is only visible from that view. That mirrors the real
/// registry closely enough for rule testing, while letting a fixture be authored once
/// and queried through either 64-bit or 32-bit rules.
/// </para>
/// <para>
/// See ADR-017 in <c>docs/DECISIONS.md</c>.
/// </para>
/// </remarks>
public sealed class FakeRegistryReader : IRegistryReader
{
    private readonly Dictionary<string, FakeRegistryKey> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FakeRegistryKey> _roots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates an empty registry.
    /// </summary>
    public FakeRegistryReader()
    {
    }

    /// <summary>
    /// Creates a registry from a fixture file.
    /// </summary>
    /// <param name="fixturePath">The path to a <c>.reg</c>-style fixture file.</param>
    public static FakeRegistryReader FromFixture(string fixturePath) =>
        RegFileFixtureParser.Parse(File.ReadAllText(fixturePath));

    /// <summary>
    /// Creates a registry from fixture text.
    /// </summary>
    public static FakeRegistryReader FromFixtureText(string text) =>
        RegFileFixtureParser.Parse(text);

    /// <summary>
    /// Adds a key, creating any missing parent keys.
    /// </summary>
    /// <returns>The newly created (or existing) key node.</returns>
    public FakeRegistryKey AddKey(
        RegistryHive hive,
        string keyPath,
        RegistryViewScope view = RegistryViewScope.Default)
    {
        var root = GetOrAddRoot(hive, view);
        var current = root;
        var built = string.Empty;

        foreach (var segment in SplitPreservingCase(keyPath))
        {
            current = current.GetOrAddSubKey(segment);
            built = built.Length == 0 ? segment : $"{built}\\{segment}";

            // Register every ancestor as well: a key exists in the registry when one
            // of its children does.
            _keys[BuildKey(hive, view, built)] = current;
        }

        return current;
    }

    /// <summary>
    /// Adds a value to a key, creating the key if necessary.
    /// </summary>
    public FakeRegistryReader AddValue(
        RegistryHive hive,
        string keyPath,
        string name,
        RegistryValueKind kind,
        object data,
        RegistryViewScope view = RegistryViewScope.Default)
    {
        var key = AddKey(hive, keyPath, view);
        key.SetValue(new RegistryValue(name, kind, data, EstimateSize(kind, data)));
        return this;
    }

    /// <summary>
    /// Adds a string value to a key.
    /// </summary>
    public FakeRegistryReader AddString(
        RegistryHive hive,
        string keyPath,
        string name,
        string value,
        RegistryViewScope view = RegistryViewScope.Default) =>
        AddValue(hive, keyPath, name, RegistryValueKind.String, value, view);

    /// <summary>
    /// Builds a location that refers to a key.
    /// </summary>
    public static RegistryLocation KeyLocation(
        RegistryHive hive,
        string keyPath,
        RegistryViewScope view = RegistryViewScope.Default) =>
        new(hive, keyPath, null, view);

    /// <summary>
    /// Builds a location that refers to a value inside a key.
    /// </summary>
    public static RegistryLocation ValueLocation(
        RegistryHive hive,
        string keyPath,
        string valueName,
        RegistryViewScope view = RegistryViewScope.Default) =>
        new(hive, keyPath, valueName, view);

    /// <inheritdoc />
    public bool KeyExists(RegistryLocation location) => TryGetKey(location, out _);

    /// <inheritdoc />
    public IReadOnlyList<string> GetSubKeyNames(RegistryLocation key) =>
        TryGetKey(key, out var node)
            ? node.SubKeys.Keys.ToArray()
            : Array.Empty<string>();

    /// <inheritdoc />
    public IReadOnlyList<string> GetValueNames(RegistryLocation key) =>
        TryGetKey(key, out var node)
            ? node.Values.Keys.ToArray()
            : Array.Empty<string>();

    /// <inheritdoc />
    public RegistryValue? GetValue(RegistryLocation location)
    {
        if (location.ValueName is null)
        {
            return null;
        }

        if (!TryGetKey(location.AsKey(), out var node))
        {
            return null;
        }

        return node.Values.TryGetValue(location.ValueName, out var value) ? value : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<RegistryLocation> EnumerateSubKeys(RegistryLocation root)
    {
        if (!TryGetKey(root, out var node))
        {
            return Array.Empty<RegistryLocation>();
        }

        var results = new List<RegistryLocation>();
        Walk(node, root.Hive, root.View, root.KeyPath, results);
        return results;
    }

    private void Walk(
        FakeRegistryKey node,
        RegistryHive hive,
        RegistryViewScope view,
        string parentPath,
        List<RegistryLocation> results)
    {
        foreach (var subKey in node.SubKeys.Values)
        {
            var path = string.IsNullOrEmpty(parentPath) ? subKey.Name : $"{parentPath}\\{subKey.Name}";
            results.Add(new RegistryLocation(hive, path, null, view));
            Walk(subKey, hive, view, path, results);
        }
    }

    private bool TryGetKey(RegistryLocation location, out FakeRegistryKey node)
    {
        if (_keys.TryGetValue(BuildKey(location.Hive, location.View, location.KeyPath), out var found))
        {
            node = found;
            return true;
        }

        // A key authored in the default view is visible from any view.
        if (location.View != RegistryViewScope.Default &&
            _keys.TryGetValue(BuildKey(location.Hive, RegistryViewScope.Default, location.KeyPath), out found))
        {
            node = found;
            return true;
        }

        node = null!;
        return false;
    }

    private static string BuildKey(RegistryHive hive, RegistryViewScope view, string path) =>
        $"{hive}|{view}|{RegistryPathNormalizer.Normalize(path)}";

    /// <summary>
    /// Returns the shared root node for a hive and view, so that repeated
    /// <see cref="AddKey"/> calls build one tree rather than separate ones.
    /// </summary>
    private FakeRegistryKey GetOrAddRoot(RegistryHive hive, RegistryViewScope view)
    {
        var key = $"{hive}|{view}";

        if (!_roots.TryGetValue(key, out var root))
        {
            root = new FakeRegistryKey(string.Empty, hive, view);
            _roots[key] = root;
        }

        return root;
    }

    /// <summary>
    /// Splits a key path into segments, preserving the original casing while still
    /// resolving <c>.</c> and <c>..</c> segments.
    /// </summary>
    private static IReadOnlyList<string> SplitPreservingCase(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Array.Empty<string>();
        }

        var parts = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var segments = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            if (part is ".")
            {
                continue;
            }

            if (part is "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(part);
        }

        return segments;
    }

    private static long EstimateSize(RegistryValueKind kind, object data) => kind switch
    {
        RegistryValueKind.String or RegistryValueKind.ExpandString =>
            data is string text ? (text.Length + 1) * sizeof(char) : 0,
        RegistryValueKind.MultiString => data is string[] entries
            ? (entries.Sum(e => e.Length + 1) + 1) * sizeof(char)
            : 0,
        RegistryValueKind.Binary => data is byte[] bytes ? bytes.Length : 0,
        RegistryValueKind.DWord => sizeof(uint),
        RegistryValueKind.QWord => sizeof(ulong),
        _ => 0,
    };
}
