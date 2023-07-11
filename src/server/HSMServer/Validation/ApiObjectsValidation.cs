using HSMSensorDataObjects.HistoryRequests;

namespace HSMServer.Validation
{
    internal static class ApiObjectsValidation
    {
        internal static bool TryValidate(this HistoryRequest request, out string message)
        {
            if (request.To.HasValue ^ request.Count.HasValue || (request.To.HasValue && request.Count == 0))
            {
                message = string.Empty;
                return true;
            }

            message = $"Request should contain only '{nameof(request.To)}' or '{nameof(request.Count)}'";
            return false;
        }
    }
}
