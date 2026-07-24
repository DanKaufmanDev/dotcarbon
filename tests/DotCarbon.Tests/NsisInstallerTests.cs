using DotCarbon.Cli.Bundling;
using DotCarbon.Core.Config;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 8.7: the NSIS <c>.exe</c> installer, a second Windows target beside the WiX MSI. The script is
/// generated on any OS, so its contents are covered here; compiling it with makensis and running the
/// silent install/uninstall happens on the Windows CI runner, which is also how the MSI is verified.
/// </summary>
public class NsisInstallerTests
{
    private static CarbonConfig Config(Action<CarbonConfig>? customize = null)
    {
        var config = new CarbonConfig
        {
            App = { Name = "Demo App", Version = "1.2.3", Identifier = "com.example.demo" },
        };
        customize?.Invoke(config);
        return config;
    }

    private static string Script(CarbonConfig config, string? webView2 = null, string? icon = null) =>
        NsisInstaller.Script(config, @"C:\out\win-x64", "Demo App.exe", webView2, icon, @"C:\out\Demo App-setup.exe");

    [Fact]
    public void The_script_declares_the_app_identity_and_output()
    {
        var script = Script(Config());

        Assert.Contains("Name \"Demo App\"", script);
        Assert.Contains(@"OutFile ""C:\out\Demo App-setup.exe""", script);
        Assert.Contains("VIProductVersion \"1.2.3.0\"", script);
    }

    [Theory]
    // VIProductVersion demands exactly four numeric parts, so anything else has to be normalized.
    [InlineData("1.2.3", "1.2.3.0")]
    [InlineData("1.2", "1.2.0.0")]
    [InlineData("2", "2.0.0.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData("1.2.3-beta.1", "1.2.3.0")]
    [InlineData("", "0.0.0.0")]
    public void Versions_are_normalized_to_four_numeric_parts(string version, string expected) =>
        Assert.Equal(expected, NsisInstaller.FileVersion(version));

    [Fact]
    public void Per_user_is_the_default_and_needs_no_elevation()
    {
        // A per-user install avoids a UAC prompt, which is what most desktop apps want.
        var script = Script(Config());

        Assert.Contains(@"InstallDir ""$LOCALAPPDATA\Programs\Demo App""", script);
        Assert.Contains("RequestExecutionLevel user", script);
    }

    [Fact]
    public void Per_machine_installs_to_program_files_and_asks_for_admin()
    {
        var script = Script(Config(c => c.Bundle.Windows.Nsis.InstallMode = "perMachine"));

        Assert.Contains(@"InstallDir ""$PROGRAMFILES64\Demo App""", script);
        Assert.Contains("RequestExecutionLevel admin", script);
    }

    [Fact]
    public void It_installs_the_published_output_and_writes_an_uninstaller()
    {
        var script = Script(Config());

        Assert.Contains(@"File /r ""C:\out\win-x64\*.*""", script);
        Assert.Contains(@"WriteUninstaller ""$INSTDIR\uninstall.exe""", script);
        Assert.Contains("Section \"Uninstall\"", script);
        Assert.Contains(@"RMDir /r ""$INSTDIR""", script);
    }

    [Fact]
    public void It_registers_an_add_remove_programs_entry_keyed_on_the_identifier()
    {
        // Keyed on the identifier rather than the name, so a rename does not orphan the old entry.
        var script = Script(Config());

        Assert.Contains(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\com.example.demo", script);
        Assert.Contains("\"DisplayName\" \"Demo App\"", script);
        Assert.Contains("\"DisplayVersion\" \"1.2.3\"", script);
        Assert.Contains("\"QuietUninstallString\"", script);
    }

    [Fact]
    public void Nsis_variables_in_registry_values_are_left_unescaped()
    {
        // Escape() doubles '$' for user text; applying it to $INSTDIR would write a literal "$$INSTDIR"
        // into the registry and break uninstall from Add/Remove Programs.
        var script = Script(Config());

        Assert.Contains("\"InstallLocation\" \"$INSTDIR\"", script);
        Assert.DoesNotContain("$$INSTDIR", script);
    }

    [Fact]
    public void The_webview2_bootstrapper_is_run_and_then_removed()
    {
        var script = Script(Config(), webView2: @"C:\out\MicrosoftEdgeWebview2Setup.exe");

        Assert.Contains("MicrosoftEdgeWebview2Setup.exe", script);
        Assert.Contains("/silent /install", script);
        Assert.Contains(@"Delete ""$INSTDIR\MicrosoftEdgeWebview2Setup.exe""", script);
    }

    [Fact]
    public void Without_webview2_no_bootstrapper_step_is_emitted()
    {
        var script = Script(Config());

        Assert.DoesNotContain("/silent /install", script);
    }

    [Fact]
    public void File_associations_are_registered_and_removed_again_on_uninstall()
    {
        var script = Script(Config(c => c.Bundle.FileAssociations.Add(
            new FileAssociationConfig { Extensions = ["demo", "dmo"], Description = "Demo file" })));

        Assert.Contains(@"WriteRegStr HKCU ""Software\Classes\.demo"" """" ""com.example.demo.demo""", script);
        Assert.Contains(@"WriteRegStr HKCU ""Software\Classes\.dmo""", script);
        Assert.Contains(@"DeleteRegKey HKCU ""Software\Classes\.demo""", script);
        Assert.Contains(@"DeleteRegKey HKCU ""Software\Classes\com.example.demo.demo""", script);
    }

    [Fact]
    public void Deep_link_protocols_are_registered_as_url_handlers()
    {
        var script = Script(Config(c => c.Bundle.Protocols.Add(
            new ProtocolConfig { Schemes = ["demoapp"] })));

        Assert.Contains(@"WriteRegStr HKCU ""Software\Classes\demoapp"" """" ""URL:demoapp""", script);
        Assert.Contains("\"URL Protocol\"", script);
        Assert.Contains(@"DeleteRegKey HKCU ""Software\Classes\demoapp""", script);
    }

    [Fact]
    public void An_icon_is_used_for_both_the_installer_and_uninstaller()
    {
        var script = Script(Config(), icon: @"C:\out\icon.ico");

        Assert.Contains(@"!define MUI_ICON ""C:\out\icon.ico""", script);
        Assert.Contains(@"!define MUI_UNICON ""C:\out\icon.ico""", script);
    }

    [Fact]
    public void A_license_page_is_only_added_when_a_license_is_configured()
    {
        Assert.DoesNotContain("MUI_PAGE_LICENSE", Script(Config()));

        var script = Script(Config(c => c.Bundle.Windows.Nsis.License = @"C:\LICENSE.txt"));
        Assert.Contains(@"!insertmacro MUI_PAGE_LICENSE ""C:\LICENSE.txt""", script);
    }

    [Fact]
    public void The_windows_formats_list_binds_from_carbon_json()
    {
        // The CI fixture asks for both formats via config; if this did not bind, the Windows job would
        // silently build only the MSI and the NSIS smoke would have nothing to install.
        var path = Path.Combine(Path.GetTempPath(), $"carbon-fmt-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            """
            { "app": { "name": "X" },
              "bundle": { "windows": { "formats": ["msi", "nsis"], "nsis": { "installMode": "perMachine" } } } }
            """);
        try
        {
            var config = ConfigLoader.Load(path);

            Assert.Equal(["msi", "nsis"], config.Bundle.Windows.Formats);
            Assert.Equal("perMachine", config.Bundle.Windows.Nsis.InstallMode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Windows_defaults_to_the_msi_alone()
    {
        // Adding NSIS must not change what an existing project produces without opting in.
        Assert.Equal(["msi"], new CarbonConfig().Bundle.Windows.Formats);
    }

    [Fact]
    public void Quotes_in_the_app_name_cannot_break_the_script_strings()
    {
        var script = Script(Config(c => c.App.Name = "My \"Great\" App"));

        // NSIS escapes a quote as $\" — an unescaped one would terminate the string early.
        Assert.Contains("$\\\"", script);
        Assert.DoesNotContain("Name \"My \"Great\" App\"", script);
    }
}
