using System.Runtime.InteropServices;
using System.Text;

namespace RegistryCleaner.Core.Safety.PathNormalization;

/// <summary>
/// Resolves 8.3 short names to their long form using the Win32
/// <c>GetLongPathName</c> API.
/// </summary>
/// <remarks>
/// <para>
/// <c>GetLongPathName</c> only expands names for paths that actually exist; for anything
/// else it returns the input unchanged, which is the correct fallback here.
/// </para>
/// <para>
/// Registry key paths never need this. It exists for the file paths stored inside
/// registry values, where <c>C:\PROGRA~1\App</c> and <c>C:\Program Files\App</c> name
/// the same directory.
/// </para>
/// </remarks>
public sealed class Win32PathAliasResolver : IPathAliasResolver
{
    private const int MaxPathLength = 32767;

    /// <inheritdoc />
    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        try
        {
            var buffer = new StringBuilder(MaxPathLength);
            var length = GetLongPathName(path, buffer, (uint)buffer.Capacity);

            // A return value equal to the buffer size means the buffer was too small,
            // and a zero return means the call failed. In both cases the input stands.
            if (length == 0 || length >= buffer.Capacity)
            {
                return path;
            }

            var resolved = buffer.ToString();
            return string.IsNullOrEmpty(resolved) ? path : resolved;
        }
        catch (DllNotFoundException)
        {
            return path;
        }
        catch (EntryPointNotFoundException)
        {
            return path;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(
        string lpszShortPath,
        StringBuilder lpszLongPath,
        uint cchBuffer);
}
