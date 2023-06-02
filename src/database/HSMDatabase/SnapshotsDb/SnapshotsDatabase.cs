using HSMDatabase.AccessManager;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HSMDatabase.SnapshotsDb
{
    internal sealed class SnapshotsDatabase : ISnapshotDatabase
    {
        private readonly LinkedList<SnapshotNode> _nodes = new();

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly string _mainFolder;


        internal SnapshotsDatabase(string mainFolder)
        {
            _mainFolder = mainFolder;

            var folders = new DirectoryInfo(mainFolder).GetDirectories();
            
            foreach (var folder in folders.OrderByDescending(u => u.Name))
            {
                _nodes.AddLast(new SnapshotNode(folder.FullName));
            }
        }


        public IEntitySnapshotNode BuildNode(bool isFinal)
        {
            var newNode = new SnapshotNode(_mainFolder, isFinal);

            _nodes.AddFirst(newNode);

            _logger.Info("New snapshot db has been added: {name}", newNode.FolderName);

            try
            {
                while (_nodes.Count > 2)
                {
                    var last = _nodes.Last.Value;

                    Directory.Delete(last.FolderName, true);

                    _nodes.RemoveLast();

                    _logger.Info("Old snapshot db has been removed: {name}", last.FolderName);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }

            return newNode;
        }

        public bool TryGetLastNode(out IEntitySnapshotNode node)
        {
            node = _nodes.FirstOrDefault();

            return node is not null;
        }
    }
}