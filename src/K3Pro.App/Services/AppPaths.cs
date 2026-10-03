namespace K3Pro.App.Services;

internal static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "K3Pro");


    public static string SettingsFile => Path.Combine(DataDir, "app-settings.json");
    public static string KeymapStateFile => Path.Combine(DataDir, "keymap-state.json");

    public static string LogDir => Path.Combine(DataDir, "logs");

    public static string LayoutFile => Path.Combine(AppContext.BaseDirectory, "layout.json");
}
