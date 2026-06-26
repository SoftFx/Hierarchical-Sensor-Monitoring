using HSMServer.Core.Managers;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Notifications;
using HSMServer.Notifications.Channels;
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

        private readonly ISlackDestinationsManager _destinations;
        private readonly HttpClient _httpClient;
        private readonly int _maxRetryAttempts;
        private readonly TimeSpan _initialBackoff;

        public NotificationKind Kind => NotificationKind.Slack;

        public event Action MessageSending;
        public event Action<string, string> MessageSended;
        public event Action<string> ErrorHandled;


        public SlackNotificationChannel(ISlackDestinationsManager destinations, HttpClient httpClient)
            : this(destinations, httpClient, DefaultMaxRetryAttempts, DefaultInitialBackoff, DefaultRequestTimeout)
        {
        }

        internal SlackNotificationChannel(ISlackDestinationsManager destinations, HttpClient httpClient,
            int maxRetryAttempts, TimeSpan initialBackoff, TimeSpan requestTimeout)
        {
            _destinations = destinations;
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
                    if (alert.Destination.Kind != NotificationKind.Slack)
                        continue;

                    var payload = SlackMessageBuilder.BuildPayload(alert);

                    foreach (var destinationId in alert.Destination.Chats)
                    {
                        if (!_destinations.TryGetValue(destinationId, out var destination))
                        {
                            _logger.Warn($"Slack destination {destinationId} not found for alert {alert.PolicyId}");
                            continue;
                        }

                        if (!destination.SendMessages)
                            continue;

                        await PostWithRetryAsync(destination.WebhookUrl, payload, destination.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Slack DeliverAsync failed");
            }
        }

        public Task FlushAsync() => Task.CompletedTask;


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
