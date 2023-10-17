using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Journal;
using HSMServer.Core.TableOfChanges;
using System;

namespace HSMServer.Core.Model.Policies
{
    public abstract class PolicyCollectionBase : IChangesEntity
    {
        private protected BaseNodeModel _model;

        private protected ChangeCollection AlertChangeTable => _model.ChangeTable.Policies;


        public TTLPolicy TimeToLive { get; private set; }


        public event Action<JournalRecordModel> ChangesHandler;


        internal virtual void Attach(BaseNodeModel model) => _model = model;

        internal virtual void BuildDefault(BaseNodeModel node, PolicyEntity entity = null) => TimeToLive = new TTLPolicy(node, entity);


        internal void UpdateTTL(PolicyUpdate update)
        {
            var oldValue = TimeToLive.ToString();

            TimeToLive.FullUpdate(update);

            CallJournal(update.Id, update.Id == Guid.Empty ? string.Empty : oldValue, TimeToLive.ToString(), update.Initiator, update.IsParentRequest);
        }


        protected void CallJournal(Guid alertId, string oldValue, string newValue, InitiatorInfo initiator, bool isParentCall = false)
        {
            if (oldValue != newValue)
            {
                var propertyName = isParentCall ? "Alert (change by parent)" : "Alert";

                ChangesHandler?.Invoke(new JournalRecordModel(_model.Id, initiator)
                {
                    Enviroment = "Alert collection",
                    PropertyName = propertyName,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Path = _model.FullPath,
                });

                if (alertId != Guid.Empty)
                    AlertChangeTable[alertId.ToString()].SetUpdate(initiator);
            }
        }
    }
}