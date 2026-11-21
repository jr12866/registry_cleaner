using System.Globalization;
using System.Text;
using RegistryCleaner.Core.Registry;

namespace RegistryCleaner.TestHarness;

/// <summary>
/// Parses a <c>.reg</c>-style fixture file into a <see cref="FakeRegistryReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// The format is a readable subset of the Windows Registry Editor export format:
/// </para>
/// <code>
/// ; comments start with a semicolon
/// ; @view=Registry32              sets the registry view for the keys that follow
///
/// [HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{GUID}]
/// "DisplayName"="Contoso Widget Suite"
/// "InstallLocation"="C:\\Program Files\\Contoso\\"
/// "SystemComponent"=dword:00000001
/// "Size"=qword:0000000000001000
/// "Blob"=hex:01,02,03
/// "Expanded"=hex(2):43,00,3a,00,5c,00
/// "List"=hex(7):41,00,00,00,42,00,00,00,00,00
/// </code>
/// <para>
/// Making fixtures look like real <c>.reg</c> exports keeps them readable, and keeps the
/// test data close to what a user would find in the registry.
/// </para>
/// </remarks>
public static class RegFileFixtureParser
{
    /// <summary>
    /// Parses fixture text into an in-memory registry.
    /// </summary>
    /// <exception cref="FormatException">The fixture text is malformed.</exception>
    public static FakeRegistryReader Parse(string text)
    {
        var reader = new FakeRegistryReader();
        var view = RegistryViewScope.Default;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        (RegistryHive Hive, string Path)? currentKey = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            var lineNumber = index + 1;

            if (line.Length == 0 || line.StartsWith(';'))
            {
                var directive = ReadViewDirective(line);
                if (directive is not null)
                {
                    view = directive.Value;
                }

                continue;
            }

            if (line[0] == '[')
            {
                if (!line.EndsWith(']'))
                {
                    throw new FormatException($"Line {lineNumber}: unterminated key header '{line}'.");
                }

                currentKey = ParseKeyHeader(line[1..^1], lineNumber);
                reader.AddKey(currentKey.Value.Hive, currentKey.Value.Path, view);
                continue;
            }

            if (!TryParseValueLine(line, lineNumber, out var name, out var kind, out var data))
            {
                throw new FormatException($"Line {lineNumber}: cannot parse value assignment '{line}'.");
            }

            var target = currentKey
                ?? throw new FormatException($"Line {lineNumber}: value declared before any key header.");

            reader.AddValue(target.Hive, target.Path, name, kind, data, view);
        }

        return reader;
    }

    private static RegistryViewScope? ReadViewDirective(string line)
    {
        const string marker = "@view=";
        var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var value = line[(index + marker.Length)..].Trim();
        return value.ToUpperInvariant() switch
        {
            "REGISTRY64" => RegistryViewScope.Registry64,
            "REGISTRY32" => RegistryViewScope.Registry32,
            "DEFAULT" => RegistryViewScope.Default,
            _ => null,
        };
    }

    private static (RegistryHive Hive, string Path) ParseKeyHeader(string header, int lineNumber)
    {
        var separator = header.IndexOf('\\');
        if (separator <= 0)
        {
            throw new FormatException($"Line {lineNumber}: key header '{header}' has no hive prefix.");
        }

        var hiveName = header[..separator];
        var path = header[(separator + 1)..].TrimEnd('\\');

        var hive = hiveName.ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => RegistryHive.HKEY_LOCAL_MACHINE,
            "HKEY_CURRENT_USER" or "HKCU" => RegistryHive.HKEY_CURRENT_USER,
            "HKEY_CLASSES_ROOT" or "HKCR" => RegistryHive.HKEY_CLASSES_ROOT,
            "HKEY_USERS" or "HKU" => RegistryHive.HKEY_USERS,
            "HKEY_CURRENT_CONFIG" or "HKCC" => RegistryHive.HKEY_CURRENT_CONFIG,
            _ => throw new FormatException($"Line {lineNumber}: unknown hive '{hiveName}'."),
        };

        return (hive, path);
    }

    private static bool TryParseValueLine(
        string line,
        int lineNumber,
        out string name,
        out RegistryValueKind kind,
        out object data)
    {
        name = string.Empty;
        kind = RegistryValueKind.Unknown;
        data = string.Empty;

        var equals = line.IndexOf('=', StringComparison.Ordinal);
        if (equals <= 0)
        {
            return false;
        }

        var left = line[..equals].Trim();
        var right = line[(equals + 1)..].Trim();

        if (left.Length < 2 || left[0] != '"' || left[^1] != '"')
        {
            return false;
        }

        name = left[1..^1];

        if (right.StartsWith('"'))
        {
            if (!right.EndsWith('"') || right.Length < 2)
            {
                throw new FormatException($"Line {lineNumber}: unterminated string value.");
            }

            kind = RegistryValueKind.String;
            data = right[1..^1].Replace("\\\\", "\\", StringComparison.Ordinal);
            return true;
        }

        if (right.Equals("-", StringComparison.Ordinal))
        {
            kind = RegistryValueKind.String;
            data = string.Empty;
            return true;
        }

        var colon = right.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        var type = right[..colon].Trim().ToLowerInvariant();
        var payload = right[(colon + 1)..].Trim();

        switch (type)
        {
            case "dword":
                kind = RegistryValueKind.DWord;
                data = uint.Parse(payload, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return true;

            case "qword":
                kind = RegistryValueKind.QWord;
                data = ulong.Parse(payload, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return true;

            case "hex":
                kind = RegistryValueKind.Binary;
                data = ParseHexBytes(payload, lineNumber);
                return true;

            case "hex(2)":
                kind = RegistryValueKind.ExpandString;
                data = DecodeUtf16(ParseHexBytes(payload, lineNumber));
                return true;

            case "hex(7)":
                kind = RegistryValueKind.MultiString;
                data = DecodeMultiString(ParseHexBytes(payload, lineNumber));
                return true;

            default:
                return false;
        }
    }

    private static byte[] ParseHexBytes(string payload, int lineNumber)
    {
        if (payload.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var parts = payload.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bytes = new byte[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
            {
                throw new FormatException($"Line {lineNumber}: '{parts[i]}' is not a valid hex byte.");
            }
        }

        return bytes;
    }

    private static string DecodeUtf16(byte[] bytes) => Encoding.Unicode.GetString(bytes);

    private static string[] DecodeMultiString(byte[] bytes)
    {
        var text = DecodeUtf16(bytes);
        return text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
