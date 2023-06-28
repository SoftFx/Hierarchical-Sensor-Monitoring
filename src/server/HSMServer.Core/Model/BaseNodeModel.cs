using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace HSMServer.Core.Model
{
    public abstract class BaseNodeModel
    {
        public List<JournalRecordModel> JournalRecordModels { get; set; } = new();

        public abstract PolicyCollectionBase Policies { get; }

        public SettingsCollection Settings { get; } = new();


        public Guid Id { get; }

        public Guid? AuthorId { get; }

        public DateTime CreationDate { get; }


        public ProductModel Parent { get; private set; }


        public string DisplayName { get; private set; }

        public string Description { get; private set; }

        public bool UseParentPolicies { get; private set; }


        public string RootProductName => Parent?.RootProductName ?? DisplayName;

        public string Path => Parent is null ? string.Empty : $"{Parent.Path}/{DisplayName}";


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

            if (entity.Settings != null)
                Settings.SetSettings(entity.Settings);
        }


        internal abstract bool CheckTimeout();


        protected internal BaseNodeModel AddParent(ProductModel parent)
        {
            Parent = parent;

            Settings.SetParentSettings(parent.Settings);

            return this;
        }

        protected internal void Update(BaseNodeUpdate update)
        {
            Description = ApplyUpdate(Description, update.Description);

            Settings.KeepHistory.TrySetValue(update.KeepHistory);
            Settings.SelfDestroy.TrySetValue(update.SelfDestroy);

            if (Settings.TTL.TrySetValue(update.TTL))
                CheckTimeout();
        }


        internal T ApplyUpdate<T>(T property, T update, [CallerArgumentExpression("property")] string propertyName = null)
        {
            if (update is not null && !update.Equals(property))
            {
                if (update is TimeIntervalModel updateTimeInterval && property is TimeIntervalModel propertyTimeInterval)
                {
                    string newValue;
                    string oldValue;

                    if (updateTimeInterval.Interval is TimeInterval.Custom)
                        newValue = new TimeSpan(updateTimeInterval.Ticks).ToString();
                    else
                        newValue = updateTimeInterval.Interval.ToString();

                    if (propertyTimeInterval.Interval is TimeInterval.Custom)
                        oldValue = new TimeSpan(propertyTimeInterval.Ticks).ToString();
                    else
                        oldValue = propertyTimeInterval.Interval.ToString();

                    JournalRecordModels.Add(new JournalRecordModel()
                    {
                        Id = Id,
                        Time = DateTime.UtcNow.Ticks,
                        Value = $"{propertyName}: {oldValue} -> {newValue}"
                    });
                }
                else
                    JournalRecordModels.Add(new JournalRecordModel()
                    {
                        Id = Id,
                        Time = DateTime.UtcNow.Ticks,
                        Value = $"{propertyName}: {property} -> {update}"
                    });

                return update;
            }

            return property;
        }
    }
}