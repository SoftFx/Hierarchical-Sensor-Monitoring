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
                            chat.SlackAccumulator.AddMessage(alert, message is ScheduleAlertMessage);
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
                    if (!chat.SlackAccumulator.ShouldSend(chat.MessagesAggregationTimeSec))
                        continue;

                    foreach (var notification in chat.SlackAccumulator.GetNotifications(chat.MessagesAggregationTimeSec))
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


        public async Task<ChatDeliveryResult> SendTestAsync(Chat chat)
        {
            if (string.IsNullOrEmpty(chat.SlackWebhookUrl))
                return ChatDeliveryResult.Fail("The chat has no Slack webhook.");

            var payload = SlackMessageBuilder.BuildPayload("Test message from HSM");

            // The channel never throws on a test send (#1826): the outcome carries the error.
            var error = await PostWithRetryAsync(chat.SlackWebhookUrl, payload, chat.Name);

            return error is null ? ChatDeliveryResult.Ok() : ChatDeliveryResult.Fail(error);
        }


        // Returns null on success, the terminal error text otherwise — the retry exhaustions
        // included (#1826; the last attempt's transient failure no longer escapes as an
        // exception, the callers used to catch-or-500 it).
        private async Task<string> PostWithRetryAsync(string webhookUrl, string payload, string destinationName)
        {
            var backoff = _initialBackoff;
            string lastError = null;

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
                        return null;
                    }

                    if (IsTransient(response.StatusCode) && attempt < _maxRetryAttempts)
                    {
                        lastError = $"Slack POST to {destinationName} returned {response.StatusCode}; retrying (attempt {attempt}/{_maxRetryAttempts})";
                        OnError(lastError);
                        await Task.Delay(backoff);
                        backoff *= 2;
                        continue;
                    }

                    lastError = $"Slack POST to {destinationName} returned terminal status {response.StatusCode}; giving up";
                    OnError(lastError);
                    return lastError;
                }
                catch (HttpRequestException ex)
                {
                    lastError = $"Slack POST to {destinationName} failed (attempt {attempt}/{_maxRetryAttempts}): {ex.Message}";

                    if (attempt >= _maxRetryAttempts)
                    {
                        OnError(lastError);
                        return lastError;
                    }

                    OnError(lastError);
                    await Task.Delay(backoff);
                    backoff *= 2;
                }
                catch (TaskCanceledException)
                {
                    lastError = $"Slack POST to {destinationName} timed out (attempt {attempt}/{_maxRetryAttempts})";

                    if (attempt >= _maxRetryAttempts)
                    {
                        OnError(lastError);
                        return lastError;
                    }

                    OnError(lastError);
                    await Task.Delay(backoff);
                    backoff *= 2;
                }
                catch (Exception ex)
                {
                    lastError = $"Slack POST to {destinationName} failed terminally: {ex.Message}";
                    OnError(lastError);
                    return lastError;
                }
            }

            return lastError;
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
