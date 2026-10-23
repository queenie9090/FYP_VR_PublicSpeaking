public static class UserSession
{
    public static string CurrentUserId { get; set; }
    public static string CurrentUserEmail { get; set; }

    // Configured scenario parameters from Setup UI
    public static ScenarioConfig SelectedScenarioConfig { get; set; } = new ScenarioConfig();
    public static string InputMode { get; set; } = "Hands-Tracking";

    // Session control flags
    public static bool IsRunningTutorial { get; set; } = false;

    // Active session payload for detailed analytics view
    public static NormalizedSessionData CurrentSessionData { get; set; }

    public static bool IsLoggedIn => !string.IsNullOrEmpty(CurrentUserId);
}

//public static class UserSession
//{
//    // Hardcode a default UUID for Editor testing
//    public static string CurrentUserId { get; set; } = "1f6adfaa-d20d-4bd7-837b-641ae6976d82";
//    public static string CurrentUserEmail { get; set; } = "queenie050306@gmail.com";

//    public static ScenarioConfig SelectedScenarioConfig { get; set; } = new ScenarioConfig();
//    public static string InputMode { get; set; } = "Hands-Free";
//    public static bool IsRunningTutorial { get; set; } = false;

//    public static bool IsLoggedIn => !string.IsNullOrEmpty(CurrentUserId);
//}