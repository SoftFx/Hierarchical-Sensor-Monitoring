using HSMSensorDataObjects.HistoryRequests;

namespace HSMServer.Validation
{
    internal static class ApiObjectsValidation
    {
        internal static bool TryValidate(this HistoryRequest request, out string message)
        {
            var hasTo = request.To.HasValue;
            var hasCount = request.Count.HasValue;

            if (hasTo ^ hasCount)
            {
                message = string.Empty;
                return true;
            }

            message = $"Request {(hasTo && hasCount ? "may" : "should")} contain a non-null field 'To' or 'Count'";
            return false;
        }
    }
}
