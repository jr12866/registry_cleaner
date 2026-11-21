using RegistryCleaner.Core.Findings;
using RegistryCleaner.Core.Registry;
using RegistryCleaner.Core.Safety;
using RegistryCleaner.Core.Safety.PathNormalization;

namespace RegistryCleaner.Core.Tests;

/// <summary>
/// Tests for <see cref="ProtectedPathGuard"/>.
/// </summary>
/// <remarks>
/// These are the most important tests in the project. The guard is the last thing
/// between a rule bug and a broken machine, so every bypass technique that the
/// verification checklist in <c>docs/SAFETY.md</c> §9 calls out has a test here:
/// case variants, separator variants, trailing separators, dot segments, and 8.3
/// short names.
/// </remarks>
public sealed class ProtectedPathGuardTests
{
    private static RegistryLocation Loc(
        RegistryHive hive,
        string path,
        string? value = null,
        RegistryViewScope view = RegistryViewScope.Default) =>
        new(hive, path, value, view);

    [Fact]
    public void Protectes_SamKey_IsProtected()
    {
        var guard = new ProtectedPathGuard();

        Assert.True(guard.IsProtected(Loc(RegistryHive.HKEY_LOCAL_MACHINE, @"SAM")));
    }

    [Theory]
    [InlineData(@"SYSTEM\CurrentControlSet\Services")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}")]
    [InlineData(@"SYSTEM\CurrentControlSet\Enum\DISPLAY")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\DeviceClasses\{GUID}")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\DeviceGuard")]
    [InlineData(@"SYSTEM\Setup")]
    [InlineData(@"BOOT")]
    public void Protectes_SystemSubtrees_AreProtected(string path)
    {
        var guard = new ProtectedPathGuard();

        Assert.True(guard.IsProtected(Loc(RegistryHive.HKEY_LOCAL_MACHINE, path)));
    }

    [Theory]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Cache")]
    [InlineData(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon")]
    [InlineData(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\notepad.exe")]
    [InlineData(@"SOFTWARE\Microsoft\Windows Defender")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\Packages")]
    public void Protectes_SoftwareSubtrees_AreProtected(string path)
    {
        var guard = new ProtectedPathGuard();

        Assert.True(guard.IsProtected(Loc(RegistryHive.HKEY_LOCAL_MACHINE, path)));
    }

    [Theory]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders")]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run")]
    public void Protectes_UserSubtrees_AreProtected(string path)
    {
        var guard = new ProtectedPathGuard();

        Assert.True(guard.IsProtected(
            Loc(RegistryHive.HKEY_CURRENT_USER, path)));
    }

    // --- Bypass attempts -------------------------------------------------------------

    [Theory]
    [InlineData(@"sam")]
    [InlineData(@"SAM")]
    [InlineData(@"sAm")]
    public void Bypass_CaseVariants_AreBlocked(string path)
    {
        var guard = new ProtectedPathGuard();

        Assert.True(guard.IsProtected(Loc(RegistryHive.HKEY_LOCAL_MACHINE, path)));
    }

    [Theory]
    [InlineData(@"SYSTEM/CurrentControlSet/Services")]
    [InlineData(@"SYSTEM\\CurrentControlSet\\Services")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\")]
    [InlineData(@"\SYSTEM\CurrentControlSet\Services")]
    public void Bypass_SeparatorVariants_AreBlocked(string path)
    {
        var guard = new ProtectedPathGuard();

        Assert.True(guard.IsProtected(Loc(RegistryHive.HKEY_LOCAL_MACHINE, path)));
    }

    [Theory]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\..\CurrentVersion\Installer\Cache")]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\Class\..\Class")]
    [InlineData(@".\SYSTEM\CurrentControlSet\Enum")]
    public void Bypass_DotSegments_AreBlocked(string path)
    {
        var guard = new ProtectedPathGuard();

        Assert.True(guard.IsProtected(Loc(RegistryHive.HKEY_LOCAL_MACHINE, path)));
    }

    // --- Value-scoped protections ------------------------------------------------------

    [Fact]
    public void Protectes_PendingFileRenameOperations_IsProtected()
    {
        var guard = new ProtectedPathGuard();
        var location = Loc(
            RegistryHive.HKEY_LOCAL_MACHINE,
            @"SYSTEM\CurrentControlSet\Control\Session Manager",
            "PendingFileRenameOperations");

        Assert.True(guard.IsProtected(location));
    }

    [Fact]
    public void Protectes_PendingFileRenameOperations_OtherValuesAreNotBlocked()
    {
        var guard = new ProtectedPathGuard();
        var location = Loc(
            RegistryHive.HKEY_LOCAL_MACHINE,
            @"SYSTEM\CurrentControlSet\Control\Session Manager",
            "SomethingElse");

        Assert.False(guard.IsProtected(location));
    }

    [Fact]
    public void Protectes_SessionManagerKeyItself_IsNotBlockedByValueScopedRule()
    {
        var guard = new ProtectedPathGuard();
        var location = Loc(
            RegistryHive.HKEY_LOCAL_MACHINE,
            @"SYSTEM\CurrentControlSet\Control\Session Manager");

        Assert.False(guard.IsProtected(location));
    }

    // --- Protected value names ---------------------------------------------------------

    [Theory]
    [InlineData("System")]
    [InlineData("ImagePath")]
    [InlineData("ServiceDll")]
    [InlineData("start")]
    public void Protectes_ProtectedValueNames_AreBlockedEverywhere(string valueName)
    {
        var guard = new ProtectedPathGuard();
        var location = Loc(
            RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\Some\App\Settings",
            valueName);

        Assert.True(guard.IsProtected(location));
    }

    [Fact]
    public void Protectes_UnprotectedValueName_IsAllowed()
    {
        var guard = new ProtectedPathGuard();
        var location = Loc(
            RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\Some\App\Settings",
            "InstallPath");

        Assert.False(guard.IsProtected(location));
    }

    // --- HKEY_CLASSES_ROOT expansion ---------------------------------------------------

    [Fact]
    public void Protectes_ShellInfrastructureClsid_IsProtectedViaHkcr()
    {
        var guard = new ProtectedPathGuard();
        var location = Loc(
            RegistryHive.HKEY_CLASSES_ROOT,
            @"CLSID\{6D809377-6AF0-444B-8957-A3773F02200E}");

        Assert.True(guard.IsProtected(location));
    }

    [Fact]
    public void Protectes_ShellInfrastructureClsid_IsProtectedViaBackingStore()
    {
        var guard = new ProtectedPathGuard();
        var location = Loc(
            RegistryHive.HKEY_LOCAL_MACHINE,
            @"SOFTWARE\Classes\CLSID\{6D809377-6AF0-444B-8957-A3773F02200E}");

        Assert.True(guard.IsProtected(location));
    }

    // --- Ordinary targets -------------------------------------------------------------

    [Theory]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{GUID}")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\contoso.exe")]
    [InlineData(@"SOFTWARE\Classes\CLSID\{0F1E2D3C-1111-2222-3333-444455556666}")]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\Run")]
    public void Protectes_OrdinaryTargets_AreAllowed(string path)
    {
        var guard = new ProtectedPathGuard();

        Assert.False(guard.IsProtected(Loc(RegistryHive.HKEY_LOCAL_MACHINE, path)));
    }

    // --- Risk tier behaviour -----------------------------------------------------------

    [Fact]
    public void Evaluate_AllowedTarget_KeepsProposedRisk()
    {
        var guard = new ProtectedPathGuard();
        var result = guard.Evaluate(
            Loc(RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App"),
            RiskTier.Moderate);

        Assert.False(result.IsProtected);
        Assert.Equal(RiskTier.Moderate, result.EffectiveRisk);
        Assert.True(result.Selectable);
    }

    [Fact]
    public void Evaluate_ProtectedTarget_RaisesRiskAndBlocksSelection()
    {
        var guard = new ProtectedPathGuard();
        var result = guard.Evaluate(
            Loc(RegistryHive.HKEY_LOCAL_MACHINE, @"SAM"),
            RiskTier.Safe);

        Assert.True(result.IsProtected);
        Assert.Equal(RiskTier.Critical, result.EffectiveRisk);
        Assert.False(result.Selectable);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Evaluate_NeverLowersARiskTier()
    {
        var guard = new ProtectedPathGuard();

        var result = guard.Evaluate(
            Loc(RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App"),
            RiskTier.High);

        Assert.Equal(RiskTier.High, result.EffectiveRisk);
    }

    [Fact]
    public void Evaluate_Finding_DisablesSelectionWhenProtected()
    {
        var guard = new ProtectedPathGuard();
        var finding = new Finding(
            Id: "F-1",
            RuleId: "R-001",
            Location: Loc(RegistryHive.HKEY_LOCAL_MACHINE, @"SAM"),
            Summary: "Something",
            Explanation: "Something",
            Evidence: "Something",
            Risk: RiskTier.Low,
            Confidence: 90,
            Category: FindingCategory.SystemResidue);

        var evaluated = guard.Evaluate(finding);

        Assert.Equal(RiskTier.Critical, evaluated.Risk);
        Assert.False(evaluated.Selectable);
    }

    [Fact]
    public void Evaluate_Finding_KeepsSelectionWhenAllowed()
    {
        var guard = new ProtectedPathGuard();
        var finding = new Finding(
            Id: "F-1",
            RuleId: "R-001",
            Location: Loc(RegistryHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\App"),
            Summary: "Something",
            Explanation: "Something",
            Evidence: "Something",
            Risk: RiskTier.Low,
            Confidence: 90,
            Category: FindingCategory.StaleApplicationRegistration);

        var evaluated = guard.Evaluate(finding);

        Assert.Equal(RiskTier.Low, evaluated.Risk);
        Assert.True(evaluated.Selectable);
    }

    // --- Protected file paths ------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Windows\System32\config\SAM")]
    [InlineData(@"%SystemRoot%\System32\config\SAM")]
    [InlineData(@"C:\WINDOWS\System32\config\SYSTEM")]
    [InlineData(@"C:\Windows\System32\config")]
    public void Protectes_ProtectedFilePaths_AreBlocked(string path)
    {
        var guard = new ProtectedPathGuard();

        Assert.True(guard.IsProtectedFilePath(path));
    }

    [Fact]
    public void Protectes_ProtectedFilePath_DotSegmentsAreResolved()
    {
        var guard = new ProtectedPathGuard();

        Assert.True(guard.IsProtectedFilePath(@"C:\Windows\System32\drivers\..\config\SAM"));
    }

    [Fact]
    public void Protectes_OrdinaryFilePath_IsAllowed()
    {
        var guard = new ProtectedPathGuard();

        Assert.False(guard.IsProtectedFilePath(@"C:\Program Files\Contoso\App.exe"));
    }

    [Fact]
    public void Protectes_ShortNameAlias_IsResolvedBeforeComparison()
    {
        // A path written in 8.3 short form must be recognised as the same location.
        var resolver = new StubAliasResolver("PROGRA~1", "Program Files");
        var guard = new ProtectedPathGuard(
            protectedFilePaths: [@"C:\PROGRA~1\Guarded"],
            aliasResolver: resolver);

        Assert.True(guard.IsProtectedFilePath(@"C:\PROGRA~1\Guarded\secret.dll"));
        Assert.True(guard.IsProtectedFilePath(@"C:\Program Files\Guarded\secret.dll"));
        Assert.False(guard.IsProtectedFilePath(@"C:\Program Files\Other\secret.dll"));
    }

    /// <summary>
    /// A deterministic stand-in for <see cref="Win32PathAliasResolver"/>, so the
    /// short-name handling can be tested without touching the filesystem.
    /// </summary>
    private sealed class StubAliasResolver : IPathAliasResolver
    {
        private readonly string _shortName;
        private readonly string _longName;

        public StubAliasResolver(string shortName, string longName)
        {
            _shortName = shortName;
            _longName = longName;
        }

        public string Resolve(string path) =>
            path.Replace(_shortName, _longName, StringComparison.OrdinalIgnoreCase);
    }
}
