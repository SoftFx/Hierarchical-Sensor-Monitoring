using HSMServer.Core.Managers;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Notifications;
using HSMServer.Notifications.AddressBook;
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
    public sealed class SlackNotificationChannel : INotificationChannel
    {
        internal const int DefaultMaxRetryAttempts = 3;
        internal static readonly TimeSpan DefaultInitialBackoff = TimeSpan.FromSeconds(2);
        internal static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(nameof(SlackNotificationChannel));

        private readonly IChatsManager _chats;
        private readonly HttpClient _httpClient;
        private readonly int _maxRetryAttempts;
        private readonly TimeSpan _initialBackoff;

        public NotificationKind Kind => NotificationKind.Slack;

        public event Action MessageSending;
        public event Action<string, string> MessageSended;
        public event Action<string> ErrorHandled;


        public SlackNotificationChannel(IChatsManager chats, HttpClient httpClient)
            : this(chats, httpClient, DefaultMaxRetryAttempts, DefaultInitialBackoff, DefaultRequestTimeout)
        {
        }

        internal SlackNotificationChannel(IChatsManager chats, HttpClient httpClient,
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

                        if (string.IsNullOrEmpty(chat.SlackWebhookUrl))
                            continue;

                        if (!chat.SendMessages)
                            continue;

                        if (chat.MessagesAggregationTimeSec == 0)
                        {
                            var payload = SlackMessageBuilder.BuildPayload(alert);
                            await PostWithRetryAsync(chat.SlackWebhookUrl, payload, chat.Name);
                        }
                        else
                        {
                            IMessageBuilder builder = message is ScheduleAlertMessage
                                ? chat.ScheduleMessageBuilder
                                : chat.MessageBuilder;
                            builder.AddMessage(alert);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Slack DeliverAsync failed");
            }
        }

        public async Task FlushAsync()
        {
            foreach (var chat in _chats.GetValues())
            {
                if (string.IsNullOrEmpty(chat.SlackWebhookUrl))
                    continue;

                try
                {
                    if (!chat.ShouldSendNotification)
                        continue;

                    foreach (var notification in chat.GetNotifications())
                    {
                        if (string.IsNullOrWhiteSpace(notification))
                            continue;

                        var payload = SlackMessageBuilder.BuildPayload(notification);
                        await PostWithRetryAsync(chat.SlackWebhookUrl, payload, chat.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Slack FlushAsync: error for chat {chat.Name}");
                }
            }
        }


        public async Task SendTestAsync(Chat chat)
        {
            if (string.IsNullOrEmpty(chat.SlackWebhookUrl))
                return;

            var payload = SlackMessageBuilder.BuildPayload("Test message from HSM");

            await PostWithRetryAsync(chat.SlackWebhookUrl, payload, chat.Name);
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
                        OnError($"Slack POST to {destinationName} returned {response.StatusCode}; retrying (attempt {attempt}/{_maxRetryAttempts})");
                        await Task.Delay(backoff);
                        backoff *= 2;
                        continue;
                    }

                    OnError($"Slack POST to {destinationName} returned terminal status {response.StatusCode}; giving up");
                    return;
                }
                catch (HttpRequestException ex) when (attempt < _maxRetryAttempts)
                {
                    OnError($"Slack POST to {destinationName} failed (attempt {attempt}/{_maxRetryAttempts}): {ex.Message}");
                    await Task.Delay(backoff);
                    backoff *= 2;
                }
                catch (TaskCanceledException) when (attempt < _maxRetryAttempts)
                {
                    OnError($"Slack POST to {destinationName} timed out (attempt {attempt}/{_maxRetryAttempts})");
                    await Task.Delay(backoff);
                    backoff *= 2;
                }
                catch (Exception ex)
                {
                    OnError($"Slack POST to {destinationName} failed terminally: {ex.Message}");
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
