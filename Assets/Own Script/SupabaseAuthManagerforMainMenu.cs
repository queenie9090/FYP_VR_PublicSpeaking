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

    [Header("Supabase Settings")]
    [SerializeField] private string projectUrl = "https://mfyxovdcqjxiejsrnipv.supabase.co";
    [SerializeField] private string publishableKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im1meXhvdmRjcWp4aWVqc3JuaXB2Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTIzOTEzMDIsImV4cCI6MjEwNzk2NzMwMn0.rNzJYljn48mIRbxbX5whOCBT8aoKI7FC3xsHwh_Mn-w";

    [Header("UI Panels")]
    [SerializeField] private GameObject firstPanel; // Main landing panel (Contains Login & Sign Up buttons)
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject signUpPanel;
    [SerializeField] private GameObject mainMenuPanel;

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

        // Default to landing panel immediately while initializing
        ShowFirstPanel();

        await InitializeSupabase();
    }

    private async Task InitializeSupabase()
    {
        IsReady = false;

        string trimmedUrl = projectUrl != null ? projectUrl.Trim() : "";
        string trimmedKey = publishableKey != null ? publishableKey.Trim() : "";

        if (string.IsNullOrEmpty(trimmedUrl) || string.IsNullOrEmpty(trimmedKey))
        {
            SetStatus("Please configure Supabase URL & key.", isSignUp: false);
            SetStatus("Please configure Supabase URL & key.", isSignUp: true);
            return;
        }

        try
        {
            // Simply omit SessionHandler to use Supabase's default persistence
            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = false
            };

            Client = new Supabase.Client(trimmedUrl, trimmedKey, options);
            await Client.InitializeAsync();

            IsReady = true;

            // Auto-login check: If valid session exists on disk/memory, skip landing panel
            if (Client.Auth.CurrentSession != null && !string.IsNullOrEmpty(Client.Auth.CurrentSession.AccessToken))
            {
                Debug.Log("[Auth] Active session detected! Redirecting directly to Main Menu.");
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
            SetStatus("Could not initialize Supabase.", isSignUp: false);
            SetStatus("Could not initialize Supabase.", isSignUp: true);
            Debug.LogError("Supabase initialization error: " + ex);
        }
    }

    // --- PANEL SWITCHING LOGIC ---
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

        SetStatus("Please log in.", isSignUp: false);
    }

    public void ShowSignUpPanel()
    {
        ClearAllInputs();

        if (firstPanel != null) firstPanel.SetActive(false);
        if (loginPanel != null) loginPanel.SetActive(false);
        if (signUpPanel != null) signUpPanel.SetActive(true);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);

        SetStatus("Create an account.", isSignUp: true);
    }

    // --- EMAIL / PASSWORD LOG IN ---
    // --- EMAIL / PASSWORD LOG IN ---
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
                SetStatus("Login successful!", isSignUp: false, isError: false);
                OpenMainMenuPanel();
            }
            else
            {
                SetStatus("No active session. Check credentials.", isSignUp: false, isError: true);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Login failed: " + ex.Message);

            string lowerMsg = ex.Message.ToLower();

            if (lowerMsg.Contains("invalid login credentials") || lowerMsg.Contains("invalid_credentials"))
            {
                // Clearer status text acknowledging both possibilities
                SetStatus("User not found or incorrect password.", isSignUp: false, isError: true);
            }
            else if (lowerMsg.Contains("user not found"))
            {
                SetStatus("User not found in database.", isSignUp: false, isError: true);
            }
            else if (lowerMsg.Contains("email not confirmed"))
            {
                SetStatus("Please confirm your email first.", isSignUp: false, isError: true);
            }
            else
            {
                SetStatus("Login failed. Check details.", isSignUp: false, isError: true);
            }
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
                SetStatus("Account created successfully!", isSignUp: true, isError: false);
                OpenMainMenuPanel();
            }
            else
            {
                SetStatus("Account created! Please log in.", isSignUp: false, isError: false);
                ShowLoginPanel();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Sign-up failed: " + ex.Message);

            string lowerMsg = ex.Message.ToLower();

            if (lowerMsg.Contains("already registered") || lowerMsg.Contains("user_already_exists"))
            {
                SetStatus("User already exists. Please log in.", isSignUp: true, isError: true);
            }
            else
            {
                SetStatus("Sign-up failed: " + ex.Message, isSignUp: true, isError: true);
            }
        }
        finally
        {
            isBusy = false;
        }
    }

    // --- GOOGLE OAUTH FLOW ---
    public async void ContinueWithGoogle(bool isFromSignUp)
    {
        if (!CanStartAuthentication(isFromSignUp)) return;

        isBusy = true;
        SetStatus("Opening Google Sign-In...", isFromSignUp);

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
                QueryParams = new Dictionary<string, string>
                {
                    { "prompt", "select_account" }
                }
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

            string successHtml = "<html><body style='font-family:sans-serif;text-align:center;padding-top:50px;'><h2>Login Successful!</h2><p>You can close this tab and return to Unity.</p></body></html>";
            byte[] b2 = Encoding.UTF8.GetBytes(successHtml);
            res1.ContentLength64 = b2.Length;
            res1.OutputStream.Write(b2, 0, b2.Length);
            res1.OutputStream.Close();

            if (req1.Url != null)
            {
                await Client.Auth.GetSessionFromUrl(req1.Url);

                if (Client.Auth.CurrentSession != null)
                {
                    SetStatus("Google Sign-In Successful!", isFromSignUp);
                    OpenMainMenuPanel();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Google Auth failed: " + ex.Message);
            SetStatus("Google Sign-In failed.", isFromSignUp);
        }
        finally
        {
            if (listener != null && listener.IsListening)
            {
                listener.Stop();
            }
            isBusy = false;
        }
    }

    public void ContinueWithGoogleLogin() => ContinueWithGoogle(false);
    public void ContinueWithGoogleSignUp() => ContinueWithGoogle(true);

    // --- LOGOUT FUNCTIONALITY ---
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

            PlayerPrefs.DeleteKey("UserProfile_Name");
            PlayerPrefs.DeleteKey("UserProfile_AvatarURL");
            PlayerPrefs.DeleteKey("UserProfile_Email");
            PlayerPrefs.Save();

            if (userNameText != null) userNameText.text = "";

            // Revert back to default sprite on logout instead of setting to null
            if (profileAvatarImage != null && defaultAvatarSprite != null)
            {
                profileAvatarImage.sprite = defaultAvatarSprite;
            }

            Debug.Log("[Auth] User logged out.");
            ShowFirstPanel();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Logout failed: " + ex.Message);
        }
        finally
        {
            isBusy = false;
        }
    }

    private void OpenMainMenuPanel()
    {
        if (firstPanel != null) firstPanel.SetActive(false);
        if (loginPanel != null) loginPanel.SetActive(false);
        if (signUpPanel != null) signUpPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);

        // Reset to default sprite first (prevents white box for email accounts)
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

            // Only download & replace sprite if a valid Google avatar URL exists
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
            SetStatus("Connecting to Supabase...", isSignUp);
            return false;
        }
        return !isBusy;
    }

    private bool ValidateInput(string email, string password, bool isSignUp)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            SetStatus("Enter both email and password.", isSignUp);
            return false;
        }
        if (!email.Contains("@"))
        {
            SetStatus("Enter a valid email address.", isSignUp);
            return false;
        }
        if (password.Length < 6)
        {
            SetStatus("Password must be at least 6 characters.", isSignUp);
            return false;
        }
        return true;
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
    
    // --- HELPER TO CLEAR INPUT FIELDS AND STATUS TEXTS ---
    private void ClearAllInputs()
    {
        // Clear Input Fields
        if (loginEmailInput != null) loginEmailInput.text = "";
        if (loginPasswordInput != null) loginPasswordInput.text = "";
        if (signUpEmailInput != null) signUpEmailInput.text = "";
        if (signUpPasswordInput != null) signUpPasswordInput.text = "";

        // Clear Status Texts
        if (loginStatusText != null) loginStatusText.text = "";
        if (signUpStatusText != null) signUpStatusText.text = "";
    }
}