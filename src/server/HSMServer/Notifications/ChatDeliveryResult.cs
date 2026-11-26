using System;

namespace HSMServer.Notifications
{
    /// <summary>
    /// The outcome of a chat test message (#1826): delivered, or the same error text the
    /// channel logs. The web test actions ignore it (they answer OK as before); the
    /// management surface renders it to the caller.
    /// </summary>
    public sealed record ChatDeliveryResult(bool Delivered, string Error)
    {
        public static ChatDeliveryResult Ok() => new(true, null);

        public static ChatDeliveryResult Fail(string error) => new(false, error);
    }
}
