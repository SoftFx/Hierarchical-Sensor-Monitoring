using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


namespace HSMDatabase.SnapshotsDb
{
    internal sealed class SnapshotNode : IEntitySnapshotNode
    {
        private const string FinalSuffix = "_final";

        private readonly List<IEntitySnapshotCollection> _collections = new();
        private readonly string _folderName;


        public IEntitySnapshotCollection<SensorStateEntity> Sensors { get; }

        public bool IsFinal { get; }


        internal SnapshotNode(string folder)
        {
            _folderName = folder;

            IsFinal = Path.GetFileNameWithoutExtension(folder).EndsWith(FinalSuffix);

            Sensors = Register<SensorStateEntity>(nameof(Sensors));
        }

        internal SnapshotNode(string mainFolder, bool isFinal) : this(Path.Combine(mainFolder, BuildFolderName(isFinal)))
        {
            Directory.CreateDirectory(_folderName);
        }


        public Task Save() => Task.WhenAll(_collections.Select(u => u.Save()));


        private SnapshotCollection<T> Register<T>(string name)
        {
            var collection = new SnapshotCollection<T>()
            {
                FilePath = Path.Combine(_folderName, $"{name}.json"),
            };

            _collections.Add(collection);

            return collection;
        }

        private static string BuildFolderName(bool isFinal)
        {
            return $"{DateTime.UtcNow:yyyy_MM_dd_T_HH_mm}{(isFinal ? FinalSuffix : "")}";
        }
    }
}
