using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;

namespace HSMServer.Notifications
{
    public sealed class SlackDestination : BaseServerModel<SlackDestinationEntity, SlackDestinationUpdate>
    {
        private const bool DefaultSendMessages = true;


        public string WebhookUrl { get; private set; }

        public bool SendMessages { get; private set; }


        public SlackDestination(SlackAddRequest add) : base(add)
        {
            WebhookUrl = add.WebhookUrl;
            SendMessages = DefaultSendMessages;
        }

        internal SlackDestination(SlackDestinationEntity entity) : base(entity)
        {
            WebhookUrl = entity.WebhookUrl;
            SendMessages = entity.SendMessages;
        }


        protected override void ApplyUpdate(SlackDestinationUpdate update)
        {
            WebhookUrl = update.WebhookUrl ?? WebhookUrl;
            SendMessages = update.SendMessages ?? SendMessages;
        }

        public override SlackDestinationEntity ToEntity()
        {
            var entity = base.ToEntity();

            entity.WebhookUrl = WebhookUrl;
            entity.SendMessages = SendMessages;

            return entity;
        }
    }
}
