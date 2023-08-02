using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Journal;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using System;
using System.Runtime.CompilerServices;

namespace HSMServer.Core.Model
{
    public abstract class BaseNodeModel : IChangesEntity
    {
        public abstract PolicyCollectionBase Policies { get; }

        public SettingsCollection Settings { get; } = new();


        public Guid Id { get; }

        public Guid? AuthorId { get; }

        public DateTime CreationDate { get; }


        public ProductModel Parent { get; private set; }


        public string DisplayName { get; private set; }

        public string Description { get; private set; }


        public string FullPath => Parent is null ? $"{DisplayName}" : $"{Parent.FullPath}/{DisplayName}";

        public string Path => Parent is null ? string.Empty : $"{Parent.Path}/{DisplayName}";

        public string RootProductName => Parent?.RootProductName ?? DisplayName;


        public event Action<JournalRecordModel> ChangesHandler;


        protected BaseNodeModel()
        {
            Id = Guid.NewGuid();
            CreationDate = DateTime.UtcNow;
        }

        protected BaseNodeModel(string name, Guid? authorId) : this()
        {
            DisplayName = name;
            AuthorId = authorId ?? Guid.Empty;
        }

        protected BaseNodeModel(BaseNodeEntity entity) : this()
        {
            Id = Guid.Parse(entity.Id);
            AuthorId = Guid.TryParse(entity.AuthorId, out var authorId) ? authorId : Guid.Empty;
            CreationDate = new DateTime(entity.CreationDate);

            DisplayName = entity.DisplayName;
            Description = entity.Description;

            if (entity.Settings is not null)
                Settings.SetSettings(entity.Settings);

            Settings.TTL.Uploaded += (_, _) => CheckTimeout();
        }


        internal abstract bool CheckTimeout();


        internal virtual BaseNodeModel AddParent(ProductModel parent)
        {
            Parent = parent;

            Settings.SetParentSettings(parent.Settings);

            return this;
        }

        protected internal void Update(BaseNodeUpdate update)
        {
            Description = UpdateProperty(Description, update.Description ?? Description, update.Initiator);

            Settings.Update(update, FullPath);

            if (update.TTLPolicy is not null)
            {
                Policies.UpdateTTL(update.TTLPolicy);
                CheckTimeout();
            }
        }


        protected T UpdateProperty<T>(T oldValue, T newValue, string initiator, [CallerArgumentExpression(nameof(oldValue))] string propName = "")
        {
            if (newValue is not null && !newValue.Equals(oldValue))
                ChangesHandler?.Invoke(new JournalRecordModel(Id, initiator)
                {
                    Enviroment = "General info update",
                    OldValue = $"{oldValue}",
                    NewValue = $"{newValue}",

                    PropertyName = propName,
                    Path = FullPath,
                });

            return newValue ?? oldValue;
        }
    }
}