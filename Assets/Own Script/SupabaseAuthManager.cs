using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using Supabase;

public class SupabaseAuthManager : MonoBehaviour
{
    public static SupabaseAuthManager Instance { get; private set; }

    [Header("UI Panels")]
    [SerializeField] private GameObject firstPanel;     // Landing panel (Login & Sign Up buttons)
    [SerializeField] private GameObject loginPanel;      // Email/Password Login panel
    [SerializeField] private GameObject signUpPanel;     // Email/Password Sign Up panel
    [SerializeField] private GameObject mainMenuPanel;   // Main Menu panel after login

    [Header("Login UI Fields")]
    [SerializeField] private TMP_InputField loginEmailInput;
    [SerializeField] private TMP_InputField loginPasswordInput;
    [SerializeField] private TMP_Text loginStatusText;

    [Header("Sign Up UI Fields")]
    [SerializeField] private TMP_InputField signUpEmailInput;
    [SerializeField] private TMP_InputField signUpPasswordInput;
    [SerializeField] private TMP_Text signUpStatusText;

    [Header("Main Menu Display UI")]
    [SerializeField] private TMP_Text userNameText;
    [SerializeField] private Image profileAvatarImage;
    [SerializeField] private Sprite defaultAvatarSprite;

    [Header("Status Text Styling")]
    [SerializeField] private Color successColor = Color.green;
    [SerializeField] private Color errorColor = Color.red;

    public Supabase.Client Client { get; private set; }
    public bool IsReady { get; private set; }

    private bool isBusy;

    private async void Start()
    {
        Instance = this;

        // Default to first panel immediately
        ShowFirstPanel();

        await InitializeSupabase();
    }

    private async Task InitializeSupabase()
    {
        IsReady = false;

        // Retrieve Supabase URL and Anon Key from config.json
        string trimmedUrl = ConfigLoader.GetSupabaseUrl().Trim();
        string trimmedKey = ConfigLoader.GetSupabaseAnonKey().Trim();

        if (string.IsNullOrEmpty(trimmedUrl) || string.IsNullOrEmpty(trimmedKey))
        {
            SetStatus("Please configure Supabase URL & Key in config.json.", isSignUp: false);
            SetStatus("Please configure Supabase URL & Key in config.json.", isSignUp: true);
            return;
        }

        try
        {
            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = false
            };

            Client = new Supabase.Client(trimmedUrl, trimmedKey, options);
            await Client.InitializeAsync();

            IsReady = true;

            // Auto-Login Check: If an active session is saved on disk, skip login UI
            if (Client.Auth.CurrentSession != null && !string.IsNullOrEmpty(Client.Auth.CurrentSession.AccessToken))
            {
                // Store active user ID into global UserSession
                UserSession.CurrentUserId = Client.Auth.CurrentUser.Id;
                UserSession.CurrentUserEmail = Client.Auth.CurrentUser.Email;

                Debug.Log($"[Auth] Active session found! Logged in as User ID: {UserSession.CurrentUserId}");
                OpenMainMenuPanel();
            }
            else
            {
                ShowFirstPanel();
            }
        }
        catch (Exception ex)
        {
            IsReady = false;
            SetStatus("Could not initialize authentication service.", isSignUp: false);
            SetStatus("Could not initialize authentication service.", isSignUp: true);
            Debug.LogError("[Auth] Supabase initialization error: " + ex);
        }
    }

    // --- PANEL NAVIGATION LOGIC ---
    public void ShowFirstPanel()
    {
        ClearAllInputs();
        if (firstPanel != null) firstPanel.SetActive(true);
        if (loginPanel != null) loginPanel.SetActive(false);
        if (signUpPanel != null) signUpPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
    }

    public void ShowLoginPanel()
    {
        ClearAllInputs();
        if (firstPanel != null) firstPanel.SetActive(false);
        if (loginPanel != null) loginPanel.SetActive(true);
        if (signUpPanel != null) signUpPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);

        SetStatus("Please enter your credentials.", isSignUp: false, isError: false);
    }

    public void ShowSignUpPanel()
    {
        ClearAllInputs();
        if (firstPanel != null) firstPanel.SetActive(false);
        if (loginPanel != null) loginPanel.SetActive(false);
        if (signUpPanel != null) signUpPanel.SetActive(true);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);

        SetStatus("Create a new account.", isSignUp: true, isError: false);
    }

    // --- EMAIL / PASSWORD LOGIN ---
    public async void Login()
    {
        if (!CanStartAuthentication(isSignUp: false)) return;

        string email = loginEmailInput.text.Trim();
        string password = loginPasswordInput.text;

        if (!ValidateInput(email, password, isSignUp: false)) return;

        isBusy = true;
        SetStatus("Logging in...", isSignUp: false, isError: false);

        try
        {
            var session = await Client.Auth.SignInWithPassword(email, password);

            if (session != null && !string.IsNullOrEmpty(session.AccessToken))
            {
                // Save authenticated user ID to global session tracker
                UserSession.CurrentUserId = Client.Auth.CurrentUser.Id;
                UserSession.CurrentUserEmail = Client.Auth.CurrentUser.Email;

                SetStatus("Login successful!", isSignUp: false, isError: false);
                OpenMainMenuPanel();
            }
            else
            {
                SetStatus("Login failed. No active session returned.", isSignUp: false, isError: true);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Auth] Login failed: " + ex.Message);
            string friendlyError = FormatAuthError(ex.Message, isSignUp: false);
            SetStatus(friendlyError, isSignUp: false, isError: true);
        }
        finally
        {
            isBusy = false;
        }
    }

    // --- EMAIL / PASSWORD SIGN UP ---
    public async void SignUp()
    {
        if (!CanStartAuthentication(isSignUp: true)) return;

        string email = signUpEmailInput.text.Trim();
        string password = signUpPasswordInput.text;

        if (!ValidateInput(email, password, isSignUp: true)) return;

        isBusy = true;
        SetStatus("Creating account...", isSignUp: true, isError: false);

        try
        {
            var session = await Client.Auth.SignUp(email, password);

            if (session != null && !string.IsNullOrEmpty(session.AccessToken))
            {
                // Auto-login upon registration
                UserSession.CurrentUserId = Client.Auth.CurrentUser.Id;
                UserSession.CurrentUserEmail = Client.Auth.CurrentUser.Email;

                SetStatus("Account created successfully!", isSignUp: true, isError: false);
                OpenMainMenuPanel();
            }
            else
            {
                SetStatus("Account created! Please check your email to confirm.", isSignUp: false, isError: false);
                ShowLoginPanel();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Auth] Sign-up failed: " + ex.Message);
            string friendlyError = FormatAuthError(ex.Message, isSignUp: true);
            SetStatus(friendlyError, isSignUp: true, isError: true);
        }
        finally
        {
            isBusy = false;
        }
    }

    // --- GOOGLE OAUTH SIGN IN ---
    public async void ContinueWithGoogle(bool isFromSignUp)
    {
        if (!CanStartAuthentication(isFromSignUp)) return;

        isBusy = true;
        SetStatus("Opening Google Sign-In in browser...", isFromSignUp, isError: false);

        HttpListener listener = null;

        try
        {
            listener = new HttpListener();
            string redirectUrl = "http://localhost:3000/";
            listener.Prefixes.Add(redirectUrl);
            listener.Start();

            var options = new Supabase.Gotrue.SignInOptions
            {
                RedirectTo = redirectUrl,
                QueryParams = new Dictionary<string, string> { { "prompt", "select_account" } }
            };

            var state = await Client.Auth.SignIn(Supabase.Gotrue.Constants.Provider.Google, options);

            if (!string.IsNullOrEmpty(state.Uri?.ToString()))
            {
                Application.OpenURL(state.Uri.ToString());
            }

            HttpListenerContext context1 = await listener.GetContextAsync();
            HttpListenerRequest req1 = context1.Request;
            HttpListenerResponse res1 = context1.Response;

            if (!req1.Url.Query.Contains("access_token"))
            {
                string redirectScript = @"
                    <html><body>
                    <h2>Completing sign in...</h2>
                    <script>
                        if (window.location.hash) {
                            window.location.href = 'http://localhost:3000/?' + window.location.hash.substring(1);
                        }
                    </script>
                    </body></html>";

                byte[] b1 = Encoding.UTF8.GetBytes(redirectScript);
                res1.ContentLength64 = b1.Length;
                res1.OutputStream.Write(b1, 0, b1.Length);
                res1.OutputStream.Close();

                HttpListenerContext context2 = await listener.GetContextAsync();
                req1 = context2.Request;
                res1 = context2.Response;
            }

            string successHtml = "<html><body style='font-family:sans-serif;text-align:center;padding-top:50px;'><h2>Login Successful!</h2><p>You can return to Unity now.</p></body></html>";
            byte[] b2 = Encoding.UTF8.GetBytes(successHtml);
            res1.ContentLength64 = b2.Length;
            res1.OutputStream.Write(b2, 0, b2.Length);
            res1.OutputStream.Close();

            if (req1.Url != null)
            {
                await Client.Auth.GetSessionFromUrl(req1.Url);

                if (Client.Auth.CurrentSession != null)
                {
                    UserSession.CurrentUserId = Client.Auth.CurrentUser.Id;
                    UserSession.CurrentUserEmail = Client.Auth.CurrentUser.Email;

                    SetStatus("Google Sign-In Successful!", isFromSignUp, isError: false);
                    OpenMainMenuPanel();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Auth] Google Sign-In failed: " + ex.Message);
            string friendlyError = FormatAuthError(ex.Message, isFromSignUp, isOAuth: true);
            SetStatus(friendlyError, isFromSignUp, isError: true);
        }
        finally
        {
            if (listener != null && listener.IsListening) listener.Stop();
            isBusy = false;
        }
    }

    public void ContinueWithGoogleLogin() => ContinueWithGoogle(false);
    public void ContinueWithGoogleSignUp() => ContinueWithGoogle(true);

    // --- LOGOUT LOGIC ---
    public async void Logout()
    {
        if (isBusy) return;
        isBusy = true;

        try
        {
            if (Client != null && Client.Auth.CurrentSession != null)
            {
                await Client.Auth.SignOut();
            }

            // Clear active user session
            UserSession.CurrentUserId = null;
            UserSession.CurrentUserEmail = null;

            if (userNameText != null) userNameText.text = "";
            if (profileAvatarImage != null && defaultAvatarSprite != null)
            {
                profileAvatarImage.sprite = defaultAvatarSprite;
            }

            Debug.Log("[Auth] User logged out.");
            ShowFirstPanel();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Auth] Logout failed: " + ex.Message);
        }
        finally
        {
            isBusy = false;
        }
    }

    // --- MAIN MENU PANEL INITIALIZATION ---
    private void OpenMainMenuPanel()
    {
        if (firstPanel != null) firstPanel.SetActive(false);
        if (loginPanel != null) loginPanel.SetActive(false);
        if (signUpPanel != null) signUpPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);

        if (profileAvatarImage != null && defaultAvatarSprite != null)
        {
            profileAvatarImage.sprite = defaultAvatarSprite;
        }

        var user = Client.Auth.CurrentUser;
        if (user != null)
        {
            string displayName = "User";
            string avatarUrl = "";

            if (user.UserMetadata != null)
            {
                if (user.UserMetadata.TryGetValue("full_name", out object nameObj))
                    displayName = nameObj?.ToString();
                else if (user.UserMetadata.TryGetValue("name", out object altNameObj))
                    displayName = altNameObj?.ToString();

                if (user.UserMetadata.TryGetValue("avatar_url", out object avatarObj))
                    avatarUrl = avatarObj?.ToString();
                else if (user.UserMetadata.TryGetValue("picture", out object altAvatarObj))
                    avatarUrl = altAvatarObj?.ToString();
            }

            if (string.IsNullOrEmpty(displayName) || displayName == "User")
            {
                if (!string.IsNullOrEmpty(user.Email))
                    displayName = user.Email.Split('@')[0];
            }

            if (userNameText != null)
                userNameText.text = displayName;

            if (!string.IsNullOrEmpty(avatarUrl) && profileAvatarImage != null)
            {
                StartCoroutine(DownloadAvatarTexture(avatarUrl));
            }
        }
    }

    private IEnumerator DownloadAvatarTexture(string url)
    {
        using (UnityWebRequest webRequest = UnityWebRequestTexture.GetTexture(url))
        {
            yield return webRequest.SendWebRequest();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(webRequest);
                Sprite avatarSprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f)
                );

                profileAvatarImage.sprite = avatarSprite;
            }
        }
    }

    private bool CanStartAuthentication(bool isSignUp)
    {
        if (!IsReady || Client == null)
        {
            SetStatus("Connecting to authentication service...", isSignUp, isError: true);
            return false;
        }
        return !isBusy;
    }

    private bool ValidateInput(string email, string password, bool isSignUp)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            SetStatus("Please enter both email and password.", isSignUp, isError: true);
            return false;
        }
        if (!email.Contains("@") || !email.Contains("."))
        {
            SetStatus("Please enter a valid email address.", isSignUp, isError: true);
            return false;
        }
        if (password.Length < 6)
        {
            SetStatus("Password must be at least 6 characters.", isSignUp, isError: true);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Parses raw exception messages into user-friendly status text.
    /// </summary>
    private string FormatAuthError(string rawMessage, bool isSignUp, bool isOAuth = false)
    {
        if (string.IsNullOrEmpty(rawMessage)) return "An unexpected error occurred.";

        string msg = rawMessage.ToLower();

        if (isOAuth)
        {
            if (msg.Contains("access_denied") || msg.Contains("cancelled") || msg.Contains("canceled"))
                return "Google sign-in was cancelled.";
            if (msg.Contains("listener") || msg.Contains("port") || msg.Contains("prefix"))
                return "Browser redirect failed. Please try again.";
            return "Google sign-in failed. Please try again.";
        }

        // Common Supabase/GoTrue error mappings
        if (msg.Contains("already registered") || msg.Contains("already exists") || msg.Contains("422"))
            return "An account with this email already exists.";
        if (msg.Contains("invalid login credentials") || msg.Contains("invalid_grant") || msg.Contains("400"))
            return "Incorrect email or password.";
        if (msg.Contains("email not confirmed"))
            return "Please check your inbox to confirm your email before logging in.";
        if (msg.Contains("rate limit") || msg.Contains("too many requests") || msg.Contains("429"))
            return "Too many attempts. Please wait a moment and try again.";
        if (msg.Contains("network") || msg.Contains("unable to resolve host") || msg.Contains("timed out"))
            return "Network connection issue. Check your internet connection.";

        return isSignUp ? "Sign-up failed. Please check your details." : "Login failed. Please check your credentials.";
    }

    private void SetStatus(string message, bool isSignUp, bool isError = true)
    {
        TMP_Text targetStatus = isSignUp ? signUpStatusText : loginStatusText;

        if (targetStatus != null)
        {
            targetStatus.text = message;
            targetStatus.color = isError ? errorColor : successColor;
        }

        Debug.Log($"[{(isSignUp ? "SignUp" : "Login")}] {message}");
    }

    private void ClearAllInputs()
    {
        if (loginEmailInput != null) loginEmailInput.text = "";
        if (loginPasswordInput != null) loginPasswordInput.text = "";
        if (signUpEmailInput != null) signUpEmailInput.text = "";
        if (signUpPasswordInput != null) signUpPasswordInput.text = "";

        if (loginStatusText != null) loginStatusText.text = "";
        if (signUpStatusText != null) signUpStatusText.text = "";
    }
}