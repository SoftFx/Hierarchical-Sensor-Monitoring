using HSMServer.Core.Managers;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Notifications;
using HSMServer.Notifications.Channels;
using HSMServer.Notifications.Chats;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public sealed class MattermostNotificationChannel : INotificationChannel
    {
        internal const int DefaultMaxRetryAttempts = 3;
        internal static readonly TimeSpan DefaultInitialBackoff = TimeSpan.FromSeconds(2);
        internal static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(nameof(MattermostNotificationChannel));

        private readonly IChatsManager _chats;
        private readonly HttpClient _httpClient;
        private readonly int _maxRetryAttempts;
        private readonly TimeSpan _initialBackoff;

        public NotificationKind Kind => NotificationKind.Mattermost;

        public event Action MessageSending;
        public event Action<string, string> MessageSended;
        public event Action<string> ErrorHandled;


        public MattermostNotificationChannel(IChatsManager chats, HttpClient httpClient)
            : this(chats, httpClient, DefaultMaxRetryAttempts, DefaultInitialBackoff, DefaultRequestTimeout)
        {
        }

        internal MattermostNotificationChannel(IChatsManager chats, HttpClient httpClient,
            int maxRetryAttempts, TimeSpan initialBackoff, TimeSpan requestTimeout)
        {
            _chats = chats;
            _httpClient = httpClient;
            _httpClient.Timeout = requestTimeout;
            _maxRetryAttempts = maxRetryAttempts;
            _initialBackoff = initialBackoff;
        }


        public async Task DeliverAsync(AlertMessage message)
        {
            try
            {
                foreach (var alert in message)
                {
                    foreach (var chatId in alert.Destination.Chats)
                    {
                        if (!_chats.TryGetValue(chatId, out var chat))
                            continue;

                        if (string.IsNullOrEmpty(chat.MattermostWebhookUrl))
                            continue;

                        if (!chat.SendMessages)
                            continue;

                        if (chat.MessagesAggregationTimeSec == 0)
                        {
                            var payload = MattermostMessageBuilder.BuildPayload(alert);
                            await PostWithRetryAsync(chat.MattermostWebhookUrl, payload, chat.Name);
                        }
                        else
                        {
                            chat.MattermostAccumulator.AddMessage(alert, message is ScheduleAlertMessage);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Mattermost DeliverAsync failed");
            }
        }

        public async Task FlushAsync()
        {
            foreach (var chat in _chats.GetValues())
            {
                if (string.IsNullOrEmpty(chat.MattermostWebhookUrl))
                    continue;

                try
                {
                    if (!chat.MattermostAccumulator.ShouldSend(chat.MessagesAggregationTimeSec))
                        continue;

                    foreach (var notification in chat.MattermostAccumulator.GetNotifications(chat.MessagesAggregationTimeSec))
                    {
                        if (string.IsNullOrWhiteSpace(notification))
                            continue;

                        var payload = MattermostMessageBuilder.BuildPayload(notification);
                        await PostWithRetryAsync(chat.MattermostWebhookUrl, payload, chat.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Mattermost FlushAsync: error for chat {chat.Name}");
                }
            }
        }


        public async Task SendTestAsync(Chat chat)
        {
            if (string.IsNullOrEmpty(chat.MattermostWebhookUrl))
                return;

            var payload = MattermostMessageBuilder.BuildPayload("Test message from HSM");

            await PostWithRetryAsync(chat.MattermostWebhookUrl, payload, chat.Name);
        }


        private async Task PostWithRetryAsync(string webhookUrl, string payload, string destinationName)
        {
            var backoff = _initialBackoff;

            for (var attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    MessageSending?.Invoke();

                    using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    using var response = await _httpClient.PostAsync(webhookUrl, content);

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        MessageSended?.Invoke(destinationName, payload);
                        return;
                    }

                    if (IsTransient(response.StatusCode) && attempt < _maxRetryAttempts)
                    {
                        OnError($"Mattermost POST to {destinationName} returned {response.StatusCode}; retrying (attempt {attempt}/{_maxRetryAttempts})");
                        await Task.Delay(backoff);
                        backoff *= 2;
                        continue;
                    }

                    OnError($"Mattermost POST to {destinationName} returned terminal status {response.StatusCode}; giving up");
                    return;
                }
                catch (HttpRequestException ex) when (attempt < _maxRetryAttempts)
                {
                    OnError($"Mattermost POST to {destinationName} failed (attempt {attempt}/{_maxRetryAttempts}): {ex.Message}");
                    await Task.Delay(backoff);
                    backoff *= 2;
                }
                catch (TaskCanceledException) when (attempt < _maxRetryAttempts)
                {
                    OnError($"Mattermost POST to {destinationName} timed out (attempt {attempt}/{_maxRetryAttempts})");
                    await Task.Delay(backoff);
                    backoff *= 2;
                }
                catch (Exception ex)
                {
                    OnError($"Mattermost POST to {destinationName} failed terminally: {ex.Message}");
                    return;
                }
            }
        }

        private static bool IsTransient(HttpStatusCode statusCode) =>
            statusCode >= HttpStatusCode.InternalServerError || statusCode == HttpStatusCode.RequestTimeout;

        private void OnError(string message)
        {
            _logger.Error(message);
            ErrorHandled?.Invoke(message);
        }
    }
}
