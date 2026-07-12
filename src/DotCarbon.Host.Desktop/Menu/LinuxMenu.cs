namespace DotCarbon.Host.Desktop;

/// <summary>
/// Linux app-menu placeholder. Traditional window menu bars vary by toolkit and desktop shell; this
/// backend keeps the API stable while the Linux validation pass chooses GTK menu bar vs app-menu.
/// </summary>
internal static class LinuxMenu
{
    public static void Create(CarbonMenuBuilder builder)
    {
        Console.Error.WriteLine(
            $"[Carbon] Native Linux menus are not wired yet ({builder.Groups.Count} top-level menu(s) configured).");
    }
}
