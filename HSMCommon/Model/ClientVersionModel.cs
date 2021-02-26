namespace HSMCommon.Model
{
    public class ClientVersionModel
    {
        public int MainVersion { get; set; }
        public int SubVersion { get; set; }
        public int ExtraVersion { get; set; }
        public string Postfix { get; set; }

        public static ClientVersionModel Parse(string text)
        {
            var splitRes = text.Split('.');
            ClientVersionModel result = new ClientVersionModel();
            result.MainVersion = int.Parse(splitRes[0]);
            result.SubVersion = int.Parse(splitRes[1]);
            result.ExtraVersion = int.Parse(splitRes[2]);
            result.Postfix = splitRes[3];
            return result;
        }

        public override string ToString()
        {
            return $"{MainVersion}.{SubVersion}.{ExtraVersion}.{Postfix}";
        }

        public static bool operator <(ClientVersionModel left, ClientVersionModel right)
        {
            if (left == null && right == null)
                return false;

            if (right == null)
                return false;

            if (left == null)
                return false;

            if (left.MainVersion != right.MainVersion)
            {
                return left.MainVersion < right.MainVersion;
            }

            if (left.SubVersion != right.SubVersion)
            {
                return left.SubVersion < right.SubVersion;
            }

            if (left.ExtraVersion != right.ExtraVersion)
            {
                return left.ExtraVersion < right.ExtraVersion;
            }

            if (string.IsNullOrEmpty(left.Postfix) && string.IsNullOrEmpty(right.Postfix))
                return false;

            if (string.IsNullOrEmpty(left.Postfix) && !string.IsNullOrEmpty(right.Postfix))
                return false;

            if (!string.IsNullOrEmpty(left.Postfix) && string.IsNullOrEmpty(right.Postfix))
                return true;

            return false;
        }

        public static bool operator>(ClientVersionModel left, ClientVersionModel right)
        {
            if (left < right)
                return false;

            return left.MainVersion > right.MainVersion || left.SubVersion > right.SubVersion
                || left.ExtraVersion > right.ExtraVersion ||
                                                         !left.Postfix.Equals(right.Postfix);
        }
    }
}
