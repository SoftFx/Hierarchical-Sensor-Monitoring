using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Extensions;
using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model;

namespace HSMServer.Model.FileSensors
{
    /// <summary>
    /// The file values of a File sensor for its page (#1886): the description of the last value, one value by its time, and
    /// the list of values page by page. Reads the sensor's history directly, not through the user's History tab state, so
    /// the two never share a table. The caller has checked that the user may see the sensor's product.
    /// </summary>
    public sealed class FileSensorPageService
    {
        /// <summary>Values per page of the list.</summary>
        public const int PageSize = 20;

        private const long CsvPreviewLimit = 5 * 1024 * 1024;
        private const long TxtPreviewLimit = 1024 * 1024;

        private readonly ITreeValuesCache _cache;


        public FileSensorPageService(ITreeValuesCache cache)
        {
            _cache = cache;
        }


        internal static bool IsLarge(FileValue file) => file.IsLargeFile();

        /// <summary>A text preview for an ordinary csv or txt file small enough to show; never for a large value.</summary>
        internal static bool IsPreviewEnabled(FileValue file) =>
            !IsLarge(file) &&
            ((file.Extension == "csv" && file.OriginalSize < CsvPreviewLimit) ||
             (file.Extension == "txt" && file.OriginalSize < TxtPreviewLimit));


        public FileInfoDto GetInfo(Guid sensorId) => FileInfoDto.From(GetLastValue(sensorId));

        /// <summary>
        /// The value at <paramref name="timeTicks"/> (the key of its row), read by that time alone; the last value when it is
        /// 0. Null when there is none. The content is decompressed for an ordinary file value.
        /// </summary>
        public async Task<FileValue> GetFileAsync(Guid sensorId, long timeTicks)
        {
            if (timeTicks == default)
                return GetLastValue(sensorId)?.DecompressContent();

            if (timeTicks < DateTime.MinValue.Ticks || timeTicks > DateTime.MaxValue.Ticks)
                return null;

            var time = new DateTime(timeTicks, DateTimeKind.Utc);
            var values = await _cache.GetSensorValuesPage(sensorId, time, time, 1).Flatten();

            return values.OfType<FileValue>().FirstOrDefault(file => file.Time.Ticks == timeTicks)?.DecompressContent();
        }

        /// <summary>
        /// A page of the sensor's file values, newest first: the newest <see cref="PageSize"/> without the last value
        /// (the page shows it on its own above the list), or the <see cref="PageSize"/> values older than
        /// <paramref name="olderThanTicks"/>. No limit on how far back the pages go.
        /// </summary>
        public async Task<FileValuesPageViewModel> GetPageAsync(Guid sensorId, string encodedId, long? olderThanTicks)
        {
            var sensor = _cache.GetSensor(sensorId);

            if (sensor is null)
                return new FileValuesPageViewModel { EncodedId = encodedId };

            if (olderThanTicks <= DateTime.MinValue.Ticks)
                return new FileValuesPageViewModel { EncodedId = encodedId };

            var to = olderThanTicks is { } older
                ? new DateTime(Math.Min(older - 1, DateTime.MaxValue.Ticks), DateTimeKind.Utc)
                : DateTime.MaxValue;

            var skipLast = olderThanTicks is null;
            var lastTicks = (sensor.LastValue as FileValue)?.Time.Ticks;

            // One more than a page tells whether an older page exists; one more again for the last value skipped.
            var read = await _cache.GetSensorValuesPage(sensorId, DateTime.MinValue, to, PageSize + 2).Flatten();
            var files = read.OfType<FileValue>().ToList();

            if (skipLast && files.Count > 0 && files[0].Time.Ticks == lastTicks)
                files.RemoveAt(0);

            var page = files.Take(PageSize).ToList();

            return new FileValuesPageViewModel
            {
                EncodedId = encodedId,
                Rows = page.Select(FileValueRowViewModel.From).ToList(),
                OlderThanTicks = files.Count > PageSize ? page[^1].Time.Ticks : null,
            };
        }


        private FileValue GetLastValue(Guid sensorId) => _cache.GetSensor(sensorId)?.LastValue as FileValue;
    }
}
