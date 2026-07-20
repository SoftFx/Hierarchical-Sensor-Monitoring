using HSMServer.Core.Managers;
using HSMServer.Core.Model.Policies;
using System.Text.Json;

namespace HSMServer.Notifications
{
    internal static class MattermostMessageBuilder
    {
        private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

        public static string BuildPayload(AlertResult alert)
        {
            var text = alert.ToString();

            var payload = new MattermostWebhookPayload { Text = text };

            return JsonSerializer.Serialize(payload, _options);
        }

        public static string BuildPayload(string text)
        {
            var payload = new MattermostWebhookPayload { Text = text };

            return JsonSerializer.Serialize(payload, _options);
        }

        private sealed record MattermostWebhookPayload
        {
            public string Text { get; init; }
        }
    }
}
