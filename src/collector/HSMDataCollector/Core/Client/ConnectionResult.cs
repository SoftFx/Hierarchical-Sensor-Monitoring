namespace HSMDataCollector.Core
{
    public readonly struct ConnectionResult
    {
        public static ConnectionResult Ok { get; } = new ConnectionResult();


        public string Error { get; }

        public bool Result => string.IsNullOrEmpty(Error);


        public ConnectionResult(string error)
        {
            Error = error;
        }
    }
}
