using System;

namespace HSMSensorDataObjects.HistoryRequests
{
    /// <summary>What a table history answer is written as (#1845). Serialized as a number.</summary>
    public enum TableHistoryFormat
    {
        /// <summary>One JSON object with the columns and the rows.</summary>
        Json = 0,

        /// <summary>RFC 4180 CSV with a header line.</summary>
        Csv = 1,

        /// <summary>A sequence of table blocks of the one table (the table block format), for the client library.</summary>
        Blocks = 2,
    }


    /// <summary>
    /// The rows of one table sensor in <c>[From, To)</c>, at <c>POST api/sensors/tableHistory</c> (#1845, ADR-0021 point 13).
    /// <see cref="BaseRequest.Path"/> is the sensor's path in the product of the access key. The key is read from the
    /// <c>Key</c> header only: the obsolete <see cref="BaseRequest.Key"/> of the body is not honoured on this route.
    /// </summary>
    public class TableHistoryRequest : BaseRequest
    {
        /// <summary>The first time of the range, included. UTC; a time without a kind is taken as UTC.</summary>
        public DateTime From { get; set; }

        /// <summary>The end of the range, excluded.</summary>
        public DateTime To { get; set; }

        /// <summary>The most rows the answer holds; null for the server's limit of the format, and capped by it.</summary>
        public int? MaxRows { get; set; }

        public TableHistoryFormat Format { get; set; }

        /// <summary>
        /// For <see cref="TableHistoryFormat.Blocks"/>: the newest block format version the client reads; null for 1. The
        /// server never writes a newer one, and refuses the request when it cannot write one that old.
        /// </summary>
        public int? MaxBlockFormatVersion { get; set; }
    }
}
