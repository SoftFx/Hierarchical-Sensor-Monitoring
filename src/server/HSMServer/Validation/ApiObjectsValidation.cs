using HSMSensorDataObjects.HistoryRequests;

namespace HSMServer.Validation
{
    internal static class ApiObjectsValidation
    {
        internal static bool TryValidate(this HistoryRequest request, out string message)
        {
            const string errorMsgFormat = "Request {0} contain a non-null field 'To' or 'Count'";

            message = string.Empty;

            if (request.To.HasValue)
            {
                if (request.Count.HasValue)
                {
                    message = string.Format(errorMsgFormat, "may");
                    return false;
                }

                return true;
            }

            if (!request.Count.HasValue)
            {
                message = string.Format(errorMsgFormat, "should");
                return false;
            }

            return true;
        }
    }
}
