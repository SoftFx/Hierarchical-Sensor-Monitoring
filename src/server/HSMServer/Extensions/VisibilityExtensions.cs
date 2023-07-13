namespace HSMServer.Extensions
{
    public static class VisibilityExtensions
    {
        public static string ToVisibility(this bool isVisible) => isVisible ? "d-flex" : "d-none";
    }
}
