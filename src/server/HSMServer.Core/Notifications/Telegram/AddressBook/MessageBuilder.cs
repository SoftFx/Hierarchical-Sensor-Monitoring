using HSMServer.Core.Model;
using System.Text;

namespace HSMServer.Core.Notifications
{
    internal sealed class MessageBuilder
    {
        internal string Message { get; private set; }


        internal void BuildMessage(BaseSensorModel sensor, ValidationResult oldStatus)
        {
            var builder = new StringBuilder(1 << 2);

            builder.AppendLine(sensor.ProductName);
            builder.Append($"{sensor.Path}: {oldStatus.Result} -> {sensor.ValidationResult.Result}");
            if (!sensor.ValidationResult.IsSuccess)
                builder.Append($" ({sensor.ValidationResult.Message})");

            Message = builder.ToString();
        }
    }
}
