using System;


namespace HSMServer.Core.Model.Requests
{
    public sealed record FileHistoryRequestModel : HistoryRequestModel
    {
        public string Format { get; set; }

        public bool IsArchive { get; set; }


        public FileHistoryRequestModel(Guid key, Guid productId, string path) : base(key, productId,  path) { }
    }
}
