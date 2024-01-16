using System;
using System.Net.Http;

namespace HSMDataCollector.SyncQueue
{
    internal readonly struct PackageSendingInfo
    {
        public double ContentSize { get; }

        public bool IsSuccess { get; }

        public string Error { get; }


        public PackageSendingInfo(double contentSize, HttpResponseMessage response, Exception exception = null)
        {
            ContentSize = contentSize;

            IsSuccess = response?.IsSuccessStatusCode ?? false;

            if (exception != null && response is null)
                Error = $"Error: {exception.Message}";
            else
                Error = !IsSuccess ? $"Code: {response.StatusCode}. {response.Content}" : null;
        }
    }
}