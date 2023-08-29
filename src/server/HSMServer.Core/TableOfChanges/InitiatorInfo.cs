using HSMDatabase.AccessManager.DatabaseEntities;
using System.Text;

namespace HSMServer.Core.TableOfChanges
{
    public enum InitiatorType : byte
    {
        System = 0,
        DataCollector = 10,
        User = 100,
    }


    public record InitiatorInfo
    {
        public static InitiatorInfo System { get; } = new InitiatorInfo(InitiatorType.System);


        public InitiatorType Type { get; }

        public string Info { get; }


        private InitiatorInfo(InitiatorType type, string info = null)
        {
            Type = type;
            Info = info;
        }

        public InitiatorInfo(InitiatorInfoEntity entity)
        {
            entity ??= new InitiatorInfoEntity();

            Type = (InitiatorType)entity.Type;
            Info = entity.Info;
        }


        public static InitiatorInfo AsUser(string username) => new(InitiatorType.User, username);

        public static InitiatorInfo AsCollector(string key) => new(InitiatorType.DataCollector, key);


        public InitiatorInfoEntity ToEntity() => new()
        {
            Type = (byte)Type,
            Info = Info,
        };

        public override string ToString()
        {
            var sb = new StringBuilder(1 << 5);

            sb.Append(Type.ToString());

            if (Type is not InitiatorType.System)
                sb.Append($" ({Info})");

            return sb.ToString();
        }
    }
}