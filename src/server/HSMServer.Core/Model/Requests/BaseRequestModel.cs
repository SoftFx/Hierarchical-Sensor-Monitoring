namespace HSMServer.Core.Model.Requests
{
    public abstract class BaseRequestModel
    {
        public string Key { get; init; }

        public string Path { get; init; }


        internal void Deconstruct(out string key, out string path)
        {
            key = Key;
            path = Path;
        }
    }
}
