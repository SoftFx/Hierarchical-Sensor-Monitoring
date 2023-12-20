namespace HSMServer.ConcurrentStorage
{
    public abstract record BaseRequest
    {
        public string Name { get; init; }

        public string Description { get; init; }
    }
}
