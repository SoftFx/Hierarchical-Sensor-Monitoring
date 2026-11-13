using HSMServer.SensorApi;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSMServer.ModelBinders
{
    public sealed class SensorCommandModelBinder : IModelBinder
    {
        public const string UnreadableBody = "The body cannot be read as a list of sensor commands.";


        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null)
                throw new ArgumentNullException(nameof(bindingContext));

            object value = null;

            try
            {
                // The same options the collector session reads a `commands` body with.
                value = await JsonSerializer.DeserializeAsync(bindingContext.HttpContext.Request.Body, bindingContext.ModelType, SensorApiJson.Commands);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or IOException))
            {
                // Whatever the converters throw on a body they cannot read; a request that
                // was cut off or refused by the transport is not this case and goes on up.
            }

            if (value is null)
            {
                // A body the server cannot read is the request's fault, so a model error,
                // which the API answers 400, as the collector session answers the same body
                // (#1749). Thrown from here it ended as a 500, the status that now tells a
                // collector to send the same package again: a body that can never be read.
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, UnreadableBody);
                bindingContext.Result = ModelBindingResult.Failed();

                return;
            }

            bindingContext.Result = ModelBindingResult.Success(value);
        }
    }
}
