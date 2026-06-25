using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.TableOfChanges;
using System.Collections.Generic;

namespace HSMServer.Core.Model.NodeSettings
{
    public sealed class NodeSettingsCollection : BaseSettingsCollection
    {
        public DestinationSettingProperty DefaultChats { get; } = new();

        public DestinationSettingProperty DefaultSlackDestinations { get; } = new();


        internal override void Update(BaseNodeUpdate update, ChangeInfoTable table)
        {
            base.Update(update, table);

            if (update is ProductUpdate productUpdate)
            {
                GetUpdateFunction<PolicyDestinationSettings>(update, table)(DefaultChats, productUpdate.DefaultChats, "Default telegram chats", null);
                GetUpdateFunction<PolicyDestinationSettings>(update, table)(DefaultSlackDestinations, productUpdate.DefaultSlackDestinations, "Default slack destinations", null);
            }
        }


        internal override void SetParentSettings(BaseSettingsCollection parentCollection)
        {
            base.SetParentSettings(parentCollection);

            if (parentCollection is NodeSettingsCollection settings)
            {
                DefaultChats.SetParent(settings.DefaultChats);
                DefaultSlackDestinations.SetParent(settings.DefaultSlackDestinations);
            }
        }

        internal void SetSettings(Dictionary<string, TimeIntervalEntity> settingsEntity, PolicyDestinationSettingsEntity defaultChats, PolicyDestinationSettingsEntity defaultSlackDestinations = null)
        {
            SetSettings(settingsEntity);

            DefaultChats.TrySetValue(new PolicyDestinationSettings(defaultChats));
            DefaultSlackDestinations.TrySetValue(new PolicyDestinationSettings(defaultSlackDestinations ?? new()));
        }
    }
}
