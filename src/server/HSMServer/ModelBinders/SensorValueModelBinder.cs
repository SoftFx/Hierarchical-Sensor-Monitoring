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

            // The same options the collector session reads a `list` body with.
            var value = await JsonSerializer.DeserializeAsync(bindingContext.HttpContext.Request.Body, bindingContext.ModelType, SensorApiJson.Values);

            bindingContext.Result = ModelBindingResult.Success(value);
        }
    }
}
