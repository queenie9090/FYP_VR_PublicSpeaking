public static class UserSession
{
    public static string CurrentUserId { get; set; }
    public static string CurrentUserEmail { get; set; }

    // Configured scenario parameters from Setup UI
    public static ScenarioConfig SelectedScenarioConfig { get; set; } = new ScenarioConfig();
    public static string InputMode { get; set; } = "Hands-Free";

    // Session control flags
    public static bool IsRunningTutorial { get; set; } = false;

    public static bool IsLoggedIn => !string.IsNullOrEmpty(CurrentUserId);
}