using System;
using System.Collections.Generic;


namespace HSMServer.Core.Cache
{
    public class FolderEventArgs : EventArgs
    {
        private readonly Guid _folderId;

        public List<Guid> ChatIDs { get; private set; }

        public string Error { get; set; }

        public Guid FolderId => _folderId;

        public FolderEventArgs(Guid folderId)
        {
            ChatIDs = new List<Guid>();
            _folderId = folderId;
        }
    }
}
