using HSMDatabase.AccessManager;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HSMDatabase.SnapshotsDb
{
    internal sealed class SnapshotsDatabase : ISnapshotDatabase
    {
        private readonly List<SnapshotNode> _nodes = new(1 << 2);
        private readonly string _mainFolder;


        internal SnapshotsDatabase(string mainFolder)
        {
            _mainFolder = mainFolder;

            var folders = new DirectoryInfo(mainFolder).GetDirectories();
            
            foreach (var folder in folders.OrderByDescending(u => u.Name))
            {
                _nodes.Add(new SnapshotNode(folder.FullName));
            }
        }


        public IEntitySnapshotNode BuildNode(bool isFinal)
        {
            _nodes.Add(new SnapshotNode(_mainFolder, isFinal));

            return _nodes[^1];
        }

        public bool TryGetLastNode(out IEntitySnapshotNode node)
        {
            node = _nodes.FirstOrDefault();

            return node is not null;
        }
    }
}