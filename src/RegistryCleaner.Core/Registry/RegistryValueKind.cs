namespace RegistryCleaner.Core.Registry;

/// <summary>
/// The native registry value types, mapped one-to-one so that a backup can restore
/// a value with its exact original type.
/// </summary>
public enum RegistryValueKind
{
    Unknown = 0,

    /// <summary>REG_SZ — null-terminated text.</summary>
    String,

    /// <summary>REG_EXPAND_SZ — text containing <c>%VAR%</c> references, stored unexpanded.</summary>
    ExpandString,

    /// <summary>REG_MULTI_SZ — an ordered list of strings.</summary>
    MultiString,

    /// <summary>REG_BINARY — arbitrary bytes.</summary>
    Binary,

    /// <summary>REG_DWORD — 32-bit integer.</summary>
    DWord,

    /// <summary>REG_QWORD — 64-bit integer.</summary>
    QWord,
}
