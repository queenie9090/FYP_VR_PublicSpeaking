public static class UserSession
{
    public static Supabase.Client AuthClient { get; set; }
    public static string CurrentUserId { get; set; }
    public static string CurrentUserEmail { get; set; }

    // Add this role property
    public static string UserRole { get; set; } = "student"; // Defaults to student

    // Configured scenario parameters from Setup UI
    public static ScenarioConfig SelectedScenarioConfig { get; set; } = new ScenarioConfig();
    public static string InputMode { get; set; } = "Controller"; //"Hands-Tracking";

    // Session control flags
    public static bool IsRunningTutorial { get; set; } = false;

    // Active session payload for detailed analytics view
    public static NormalizedSessionData CurrentSessionData { get; set; }
    public static NormalizedSessionData EmotionDashboardSession { get; set; }

    public static bool IsLoggedIn =>
            !string.IsNullOrEmpty(CurrentUserId) &&
            AuthClient != null &&
            AuthClient.Auth?.CurrentSession != null &&
            !string.IsNullOrEmpty(AuthClient.Auth.CurrentSession.AccessToken);

    public static bool IsEducator => UserRole?.ToLower() == "educator";

    public static void ClearAuthSession()
    {
        CurrentUserId = null;
        CurrentUserEmail = null;
        UserRole = "student";
        CurrentSessionData = null;
        EmotionDashboardSession = null;
    }

    public static bool LastSessionEndedBeforeStart = false;
    public static string LastSessionAbortReason = "";
}

//public static class UserSession
//{
//    // Hardcoded default student UUID and email for Editor testing
//    public static string CurrentUserId { get; set; } = "1f6adfaa-d20d-4bd7-837b-641ae6976d82";
//    public static string CurrentUserEmail { get; set; } = "queenie050306@gmail.com";

//    // Set default role strictly to "student"
//    public static string UserRole { get; set; } = "student";

//    // Configured scenario parameters from Setup UI
//    public static ScenarioConfig SelectedScenarioConfig { get; set; } = new ScenarioConfig();
//    public static string InputMode { get; set; } = "Hands-Free";

//    // Session control flags
//    public static bool IsRunningTutorial { get; set; } = false;

//    // Active session payload for detailed analytics view
//    public static NormalizedSessionData CurrentSessionData { get; set; }

//    public static bool IsLoggedIn => !string.IsNullOrEmpty(CurrentUserId);
//    public static bool IsEducator => UserRole?.ToLower() == "educator";
//}
