using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.Model
{
    public class McpAccessKeyModel
    {
        public Guid Id { get; }

        public Guid UserId { get; }

        public DateTime CreationTime { get; }

        public DateTime ExpirationTime { get; set; }

        public McpKeyState State { get; private set; }

        public string DisplayName { get; private init; }


        public McpAccessKeyModel()
        {
            Id = Guid.NewGuid();
            CreationTime = DateTime.UtcNow;
            State = McpKeyState.Active;
            DisplayName = string.Empty;
        }

        public McpAccessKeyModel(Guid userId)
        {
            Id = Guid.NewGuid();
            UserId = userId;
            CreationTime = DateTime.UtcNow;
            State = McpKeyState.Active;
            DisplayName = string.Empty;
        }

        public McpAccessKeyModel(Guid userId, string displayName)
        {
            Id = Guid.NewGuid();
            UserId = userId;
            CreationTime = DateTime.UtcNow;
            State = McpKeyState.Active;
            DisplayName = displayName;
        }

        public McpAccessKeyModel(McpAccessKeyEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            UserId = Guid.Parse(entity.UserId);
            State = (McpKeyState)entity.State;
            DisplayName = entity.DisplayName;
            CreationTime = new DateTime(entity.CreationTime);
            ExpirationTime = new DateTime(entity.ExpirationTime);
        }


        public McpAccessKeyModel Update(McpKeyState? state)
        {
            if (state.HasValue)
                State = state.Value;

            return this;
        }

        public bool IsValid(out string message)
        {
            message = string.Empty;

            if (State == McpKeyState.Blocked)
            {
                message = "Key is blocked.";
                return false;
            }

            if (State == McpKeyState.Expired)
            {
                message = "Key expired.";
                return false;
            }

            if (ExpirationTime < DateTime.UtcNow)
            {
                message = "Key expired.";
                return false;
            }

            return true;
        }

        public McpAccessKeyEntity ToEntity() =>
            new()
            {
                Id = Id.ToString(),
                UserId = UserId.ToString(),
                State = (byte)State,
                DisplayName = DisplayName,
                CreationTime = CreationTime.Ticks,
                ExpirationTime = ExpirationTime.Ticks,
            };
    }

    public enum McpKeyState : byte
    {
        Active = 0,
        Expired = 1,
        Blocked = 7
    }
}
