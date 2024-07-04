using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.TableOfChanges;
using System.Collections.Generic;

namespace HSMServer.Core.Model.NodeSettings
{
    public sealed class NodeSettingsCollection : BaseSettingsCollection
    {
        public DestinationSettingProperty DefaultChats { get; } = new();


        internal override void Update(BaseNodeUpdate update, ChangeInfoTable table)
        {
            base.Update(update, table);

            if (update is ProductUpdate productUpdate)
                GetUpdateFunction<PolicyDestinationSettings>(update, table)(DefaultChats, productUpdate.DefaultChats, "Default telegram chats", null);
        }


        internal override void SetParentSettings(BaseSettingsCollection parentCollection)
        {
            base.SetParentSettings(parentCollection);

            if (parentCollection is NodeSettingsCollection settings)
                DefaultChats.SetParent(settings.DefaultChats);
        }

        internal void SetSettings(Dictionary<string, TimeIntervalEntity> settingsEntity, PolicyDestinationSettingsEntity defaultChats)
        {
            SetSettings(settingsEntity);

            DefaultChats.TrySetValue(new PolicyDestinationSettings(defaultChats));
        }
    }
}
