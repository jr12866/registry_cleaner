using System.Globalization;
using RegistryCleaner.Core.Registry;

namespace RegistryCleaner.TestHarness;

/// <summary>
/// An in-memory registry key used by <see cref="FakeRegistryReader"/>.
/// </summary>
public sealed class FakeRegistryKey
{
    private readonly Dictionary<string, FakeRegistryKey> _subKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RegistryValue> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a key node.
    /// </summary>
    /// <param name="name">The key's own name (the last path segment).</param>
    /// <param name="hive">The hive the key belongs to.</param>
    /// <param name="view">The registry view the key belongs to.</param>
    public FakeRegistryKey(string name, RegistryHive hive, RegistryViewScope view)
    {
        Name = name;
        Hive = hive;
        View = view;
    }

    /// <summary>The key's own name.</summary>
    public string Name { get; }

    /// <summary>The hive the key belongs to.</summary>
    public RegistryHive Hive { get; }

    /// <summary>The registry view the key belongs to.</summary>
    public RegistryViewScope View { get; }

    /// <summary>The key's immediate subkeys, keyed by name.</summary>
    public IReadOnlyDictionary<string, FakeRegistryKey> SubKeys => _subKeys;

    /// <summary>The key's values, keyed by value name.</summary>
    public IReadOnlyDictionary<string, RegistryValue> Values => _values;

    /// <summary>Adds or replaces a value.</summary>
    public void SetValue(RegistryValue value) => _values[value.Name] = value;

    /// <summary>Adds or replaces a subkey, returning it.</summary>
    public FakeRegistryKey GetOrAddSubKey(string name)
    {
        if (_subKeys.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var created = new FakeRegistryKey(name, Hive, View);
        _subKeys[name] = created;
        return created;
    }
}
