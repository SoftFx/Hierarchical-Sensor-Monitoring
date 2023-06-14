namespace HSMDataCollector.Core
{
    public readonly struct ConnectionResult
    {
        public static ConnectionResult Ok { get; } = new ConnectionResult(null);


        public bool Result { get; }

        public string Error { get; }


        public ConnectionResult(string error)
        {
            Result = string.IsNullOrEmpty(error);
            Error = error;
        }
    }
}
