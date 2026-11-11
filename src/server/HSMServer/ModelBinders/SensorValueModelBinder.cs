using HSMServer.SensorApi;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSMServer.ModelBinders
{
    internal sealed class SensorValueModelBinder : IModelBinder
    {
        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null)
                throw new ArgumentNullException(nameof(bindingContext));

            try
            {
                // The same options the collector session reads a `list` body with.
                var value = await JsonSerializer.DeserializeAsync(bindingContext.HttpContext.Request.Body, bindingContext.ModelType, SensorApiJson.Values);

                bindingContext.Result = ModelBindingResult.Success(value);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or ArgumentException)
            {
                // One malformed element — an unknown, missing or non-numeric Type
                // discriminator, an unparseable field — is a client error about that
                // element, not a server failure. Report it through the model state so
                // the [ApiController] batch answers 400 naming the position in the
                // body, the way the session transport answers 400 "malformed body"
                // for the same body (#1672). The hand-written element converters
                // throw ArgumentException, OverflowException and others besides
                // JsonException — the session's TryReadBody catches the same set.
                var position = ex is JsonException { LineNumber: { } line, BytePositionInLine: { } offset }
                    ? $" (line {line}, byte {offset})."
                    : ".";
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                    $"Malformed sensor values: {ex.Message}{position}");
            }
        }
    }
}
