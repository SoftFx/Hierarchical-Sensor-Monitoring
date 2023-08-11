using HSMServer.ApiObjectsConverters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSMServer.ModelBinders
{
    public sealed class SensorCommandModelBinder : IModelBinder
    {
        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null)
                throw new ArgumentNullException(nameof(bindingContext));

            var serializeOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            serializeOptions.Converters.Add(new CommandRequestBaseDeserializationConverter());

            var value = await JsonSerializer.DeserializeAsync(bindingContext.HttpContext.Request.Body, bindingContext.ModelType, serializeOptions);

            bindingContext.Result = ModelBindingResult.Success(value);
        }
    }
}
