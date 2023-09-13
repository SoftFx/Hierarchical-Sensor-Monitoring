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

        public bool IsForceUpdate { get; }


        private InitiatorInfo(InitiatorType type, string info = null, bool isForce = false)
        {
            Type = type;
            Info = info;

            IsForceUpdate = isForce;
        }

        public InitiatorInfo(InitiatorInfoEntity entity)
        {
            entity ??= new InitiatorInfoEntity();

            Type = (InitiatorType)entity.Type;
            Info = entity.Info;
        }


        public static InitiatorInfo AsUser(string username) => new(InitiatorType.User, username);

        public static InitiatorInfo AsCollector(string key, bool isForce) => new(InitiatorType.DataCollector, key, isForce);


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