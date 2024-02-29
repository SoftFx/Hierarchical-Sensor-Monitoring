using System.Net.Http;

namespace HSMDataCollector.SyncQueue
{
    public readonly struct PackageSendingInfo
    {
        public double ContentSize { get; }

        public bool IsSuccess { get; }

        public string Error { get; }


        public PackageSendingInfo(double contentSize, HttpResponseMessage response)
        {
            ContentSize = contentSize;

            IsSuccess = response?.IsSuccessStatusCode ?? false;
            Error = !IsSuccess ? $"Code: {response.StatusCode}. {response.Content}" : null;
        }
    }
}