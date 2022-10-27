namespace HSMServer.Core.Model.Requests
{
    public sealed class FileHistoryRequestModel : HistoryRequestModel
    {
        public string Format { get; set; }

        public bool IsArchive { get; set; }
    }
}
