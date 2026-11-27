using HSMServer.ConcurrentStorage;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace HSMServer.Model.Folders
{
    public record FolderUpdate : IUpdateRequest
    {
        public required Guid Id { get; init; }

        public required InitiatorInfo Initiator { get; init; }


        public HashSet<Guid> Chats { get; init; }


        // Where the journals of a chats change in this update read the chat names from. Null:
        // the chat storage, as for every update. An update that follows a chat's deletion
        // carries its own — the chat is out of the storage by then, and the records must
        // still name it (#1931).
        public Func<Guid, string> ChatNameResolver { get; init; }


        public TimeIntervalViewModel TTL { get; init; }

        public TimeIntervalViewModel KeepHistory { get; init; }

        public TimeIntervalViewModel SelfDestroy { get; init; }

        public DefaultChatViewModel DefaultChats { get; init; }


        public string Description { get; init; }

        public Color? Color { get; init; }

        public string Name { get; init; }
    }
}
