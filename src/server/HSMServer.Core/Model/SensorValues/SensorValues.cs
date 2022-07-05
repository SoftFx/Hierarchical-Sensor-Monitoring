using System.IO;

namespace HSMServer.Core.Model
{
    public record BooleanValue : BaseValue<bool> { }


    public record IntegerValue : BaseValue<int> { }


    public record DoubleValue : BaseValue<double> { }


    public record StringValue : BaseValue<string> { }


    public record FileValue : BaseValue<byte[]>
    {
        private const double SizeDenominator = 1024.0;


        public string Name { get; init; }

        public string Extension { get; init; }

        public long OriginalSize { get; init; }


        public override string ToString()
        {
            string sizeString = FileSizeToNormalString(GetFileSize());
            string fileNameString = GetFileNameString(Name, Extension);

            return $"File size: {sizeString}. {fileNameString}";
        }

        private long GetFileSize() =>
            OriginalSize == 0 ? Value?.Length ?? 0 : OriginalSize;

        private static string FileSizeToNormalString(long size)
        {
            if (size < SizeDenominator)
                return $"{size} bytes";

            double kb = size / SizeDenominator;
            if (kb < SizeDenominator)
                return $"{kb:#,##0} KB";

            double mb = kb / SizeDenominator;
            if (mb < SizeDenominator)
                return $"{mb:#,##0.0} MB";

            double gb = mb / SizeDenominator;
            return $"{gb:#,##0.0} GB";
        }

        private static string GetFileNameString(string fileName, string extension)
        {
            if (string.IsNullOrEmpty(extension) && string.IsNullOrEmpty(fileName))
                return "No file info specified!";

            if (string.IsNullOrEmpty(fileName))
                return $"Extension: {extension}.";

            if (!string.IsNullOrEmpty(Path.GetExtension(fileName)))
                return $"File name: {fileName}.";

            return $"File name: {Path.ChangeExtension(fileName, extension)}.";
        }
    }


    public record IntegerBarValue : BarBaseValue<int> { }


    public record DoubleBarValue : BarBaseValue<double> { }
}
