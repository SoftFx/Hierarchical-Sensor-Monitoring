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


        public IEntitySnapshotCollection<SensorStateEntity> Sensors { get; }

        public string FolderName { get; }

        public bool IsFinal { get; }


        internal SnapshotNode(string folder)
        {
            FolderName = folder;
            IsFinal = Path.GetFileNameWithoutExtension(folder).EndsWith(FinalSuffix);

            Sensors = Register<SensorStateEntity>(nameof(Sensors));
        }

        internal SnapshotNode(string mainFolder, bool isFinal) : this(Path.Combine(mainFolder, BuildFolderName(isFinal)))
        {
            Directory.CreateDirectory(FolderName);
        }


        public Task Save() => Task.WhenAll(_collections.Select(u => u.Save()));


        private SnapshotCollection<T> Register<T>(string name)
        {
            var collection = new SnapshotCollection<T>()
            {
                FilePath = Path.Combine(FolderName, name),
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
