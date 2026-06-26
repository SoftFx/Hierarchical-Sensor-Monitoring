using HSMServer.Core.Managers;
using HSMServer.Core.Model.Policies;
using System.Text.Json;

namespace HSMServer.Notifications
{
    internal static class SlackMessageBuilder
    {
        private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

        public static string BuildPayload(AlertResult alert)
        {
            var text = alert.ToString();

            var payload = new SlackWebhookPayload { Text = text };

            return JsonSerializer.Serialize(payload, _options);
        }

        private sealed record SlackWebhookPayload
        {
            public string Text { get; init; }
        }
    }
}
