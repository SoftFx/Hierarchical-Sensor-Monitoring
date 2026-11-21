namespace HSMServer.Model.ViewModel
{
    public class LoginViewModel
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public bool KeepLoggedIn { get; set; }

        // The provider name on the SSO button; null when SSO is off (no button).
        public string SsoDisplayName { get; set; }

        // Why the last SSO sign-in failed, shown on the login page.
        public string SsoError { get; set; }
    }
}
