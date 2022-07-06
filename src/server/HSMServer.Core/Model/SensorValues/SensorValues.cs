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

        public override string ShortInfo => GetShortDescription();


        private string GetShortDescription()
        {
            string sizeString = FileSizeToNormalString();
            string fileNameString = GetFileNameString();

            return $"File size: {sizeString}. {fileNameString}";
        }

        private string FileSizeToNormalString()
        {
            if (OriginalSize < SizeDenominator)
                return $"{OriginalSize} bytes";

            double kb = OriginalSize / SizeDenominator;
            if (kb < SizeDenominator)
                return $"{kb:#,##0} KB";

            double mb = kb / SizeDenominator;
            if (mb < SizeDenominator)
                return $"{mb:#,##0.0} MB";

            double gb = mb / SizeDenominator;
            return $"{gb:#,##0.0} GB";
        }

        private string GetFileNameString()
        {
            if (string.IsNullOrEmpty(Extension) && string.IsNullOrEmpty(Name))
                return "No file info specified!";

            if (string.IsNullOrEmpty(Name))
                return $"Extension: {Extension}.";

            if (!string.IsNullOrEmpty(Path.GetExtension(Name)))
                return $"File name: {Name}.";

            return $"File name: {Path.ChangeExtension(Name, Extension)}.";
        }
    }


    public record IntegerBarValue : BarBaseValue<int> { }


    public record DoubleBarValue : BarBaseValue<double> { }
}
