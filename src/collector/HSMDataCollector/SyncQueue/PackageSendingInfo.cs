using System;
using System.Net.Http;

namespace HSMDataCollector.SyncQueue
{
    internal readonly struct PackageSendingInfo
    {
        public double ContentSize { get; }

        public bool IsSuccess { get; }

        public string Error { get; }


        public PackageSendingInfo(double contentSize, HttpResponseMessage response)
        {
            ContentSize = contentSize;
            IsSuccess = response.IsSuccessStatusCode;

            Error = !IsSuccess ? $"Code: {response.StatusCode}. {response.Content}" : null;
        }
    }
}