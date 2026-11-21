using RegistryCleaner.Core.Registry;
using RegistryCleaner.TestHarness;

namespace RegistryCleaner.Core.Tests;

/// <summary>
/// Tests for <see cref="FakeRegistryReader"/> and <see cref="RegFileFixtureParser"/>.
/// </summary>
/// <remarks>
/// The harness is the foundation of every rule test, so it needs to be trustworthy:
/// a fixture that parses differently from a real registry export would make every rule
/// test meaningless.
/// </remarks>
public sealed class FakeRegistryReaderTests
{
    [Fact]
    public void AddKey_CreatesIntermediateKeys()
    {
        var reader = new FakeRegistryReader();

        reader.AddKey(RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App\Sub");

        var parent = FakeRegistryReader.KeyLocation(
            RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App");

        Assert.True(reader.KeyExists(parent));
        Assert.Equal(["Sub"], reader.GetSubKeyNames(parent));
    }

    [Fact]
    public void AddValue_StoresValueWithTypeAndSize()
    {
        var reader = new FakeRegistryReader();

        reader.AddValue(
            RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\App",
            "InstallLocation",
            RegistryValueKind.String,
            @"C:\Program Files\App\");

        var value = reader.GetValue(
            FakeRegistryReader.ValueLocation(
                RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", "InstallLocation"));

        Assert.NotNull(value);
        Assert.Equal(RegistryValueKind.String, value!.Value.Kind);
        Assert.Equal(@"C:\Program Files\App\", value.Value.AsString());
        Assert.True(value.Value.ByteSize > 0);
    }

    [Fact]
    public void GetValue_ReturnsNullForMissingValue()
    {
        var reader = new FakeRegistryReader();

        reader.AddKey(RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App");

        var value = reader.GetValue(
            FakeRegistryReader.ValueLocation(
                RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", "Nope"));

        Assert.Null(value);
    }

    [Fact]
    public void EnumerateSubKeys_ReturnsAllDescendants()
    {
        var reader = new FakeRegistryReader();

        reader.AddKey(RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App\Sub\Deep");
        reader.AddKey(RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App\Other");

        var keys = reader.EnumerateSubKeys(
            FakeRegistryReader.KeyLocation(RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App"));

        Assert.Contains(keys, k => k.KeyPath.EndsWith(@"App\Sub", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(keys, k => k.KeyPath.EndsWith(@"App\Sub\Deep", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(keys, k => k.KeyPath.EndsWith(@"App\Other", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DefaultViewKey_IsVisibleFromEitherView()
    {
        var reader = new FakeRegistryReader();

        reader.AddKey(RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App");

        Assert.True(reader.KeyExists(FakeRegistryReader.KeyLocation(
            RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", RegistryViewScope.Registry64)));
        Assert.True(reader.KeyExists(FakeRegistryReader.KeyLocation(
            RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", RegistryViewScope.Registry32)));
    }

    [Fact]
    public void ViewSpecificKey_IsNotVisibleFromTheOtherView()
    {
        var reader = new FakeRegistryReader();

        reader.AddKey(RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", RegistryViewScope.Registry32);

        Assert.True(reader.KeyExists(FakeRegistryReader.KeyLocation(
            RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", RegistryViewScope.Registry32)));
        Assert.False(reader.KeyExists(FakeRegistryReader.KeyLocation(
            RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", RegistryViewScope.Registry64)));
    }

    [Fact]
    public void Parse_ReadsStringValues()
    {
        var reader = FakeRegistryReader.FromFixtureText(
            """
            [HKEY_LOCAL_MACHINE\SOFTWARE\App]
            "DisplayName"="Contoso Widget Suite"
            """);

        var value = reader.GetValue(
            FakeRegistryReader.ValueLocation(
                RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", "DisplayName"));

        Assert.Equal("Contoso Widget Suite", value!.Value.AsString());
    }

    [Fact]
    public void Parse_ReadsTypedValues()
    {
        var reader = FakeRegistryReader.FromFixtureText(
            """
            [HKEY_LOCAL_MACHINE\SOFTWARE\App]
            "SystemComponent"=dword:00000001
            "Size"=qword:0000000000001000
            "Blob"=hex:01,02,03
            """);

        var dword = reader.GetValue(FakeRegistryReader.ValueLocation(
            RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", "SystemComponent"));
        var qword = reader.GetValue(FakeRegistryReader.ValueLocation(
            RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", "Size"));
        var binary = reader.GetValue(FakeRegistryReader.ValueLocation(
            RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", "Blob"));

        Assert.Equal(RegistryValueKind.DWord, dword!.Value.Kind);
        Assert.Equal(1u, dword.Value.Data);
        Assert.Equal(RegistryValueKind.QWord, qword!.Value.Kind);
        Assert.Equal(4096ul, qword.Value.Data);
        Assert.Equal(RegistryValueKind.Binary, binary!.Value.Kind);
        Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])binary.Value.Data);
    }

    [Fact]
    public void Parse_ReadsExpandStringAndMultiString()
    {
        var reader = FakeRegistryReader.FromFixtureText(
            """
            [HKEY_LOCAL_MACHINE\SOFTWARE\App]
            "Expanded"=hex(2):43,00,3a,00,5c,00,41,00,42,00,43,00
            "List"=hex(7):41,00,00,00,42,00,00,00,00,00
            """);

        var expand = reader.GetValue(FakeRegistryReader.ValueLocation(
            RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", "Expanded"));
        var multi = reader.GetValue(FakeRegistryReader.ValueLocation(
            RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", "List"));

        Assert.Equal(RegistryValueKind.ExpandString, expand!.Value.Kind);
        Assert.Equal("C:\\ABC", expand.Value.AsString());
        Assert.Equal(RegistryValueKind.MultiString, multi!.Value.Kind);
        Assert.Equal(["A", "B"], multi.Value.AsMultiString());
    }

    [Fact]
    public void Parse_AppliesViewDirective()
    {
        var reader = FakeRegistryReader.FromFixtureText(
            """
            ; @view=Registry32
            [HKEY_LOCAL_MACHINE\SOFTWARE\App]
            """);

        Assert.True(reader.KeyExists(FakeRegistryReader.KeyLocation(
            RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", RegistryViewScope.Registry32)));
        Assert.False(reader.KeyExists(FakeRegistryReader.KeyLocation(
            RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", RegistryViewScope.Registry64)));
    }

    [Fact]
    public void Parse_IgnoresCommentsAndBlankLines()
    {
        var reader = FakeRegistryReader.FromFixtureText(
            """
            ; a comment

            [HKEY_LOCAL_MACHINE\SOFTWARE\App]
            ; another comment
            "Name"="Value"
            """);

        Assert.NotNull(reader.GetValue(FakeRegistryReader.ValueLocation(
            RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App", "Name")));
    }

    [Fact]
    public void Parse_RejectsValueBeforeAnyKey()
    {
        var exception = Assert.Throws<FormatException>(
            () => FakeRegistryReader.FromFixtureText("\"Name\"=\"Value\""));

        Assert.Contains("before any key header", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsUnknownHive()
    {
        var exception = Assert.Throws<FormatException>(
            () => FakeRegistryReader.FromFixtureText("[HKEY_NOWHERE\\App]"));

        Assert.Contains("unknown hive", exception.Message, StringComparison.Ordinal);
    }
}
