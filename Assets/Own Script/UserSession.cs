//public static class UserSession
//{
//    public static string CurrentUserId { get; set; }
//    public static string CurrentUserEmail { get; set; }

//    // Configured scenario parameters from Setup UI
//    public static ScenarioConfig SelectedScenarioConfig { get; set; } = new ScenarioConfig();
//    public static string InputMode { get; set; } = "Hands-Free";

//    // Session control flags
//    public static bool IsRunningTutorial { get; set; } = false;

//    public static bool IsLoggedIn => !string.IsNullOrEmpty(CurrentUserId);
//}

public static class UserSession
{
    // Hardcode a default UUID for Editor testing
    public static string CurrentUserId { get; set; } = "6571afbc-7220-4aa6-8979-2d223908f382";
    public static string CurrentUserEmail { get; set; } = "test123@gmail.com";

    public static ScenarioConfig SelectedScenarioConfig { get; set; } = new ScenarioConfig();
    public static string InputMode { get; set; } = "Hands-Free";
    public static bool IsRunningTutorial { get; set; } = false;

    public static bool IsLoggedIn => !string.IsNullOrEmpty(CurrentUserId);
}