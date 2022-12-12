using HSMServer.ApiObjectsConverters;
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

            var serializeOptions = new JsonSerializerOptions();
            serializeOptions.Converters.Add(new SensorValueBaseDeserializationConverter());

            var value = await JsonSerializer.DeserializeAsync(bindingContext.HttpContext.Request.Body, bindingContext.ModelType, serializeOptions);

            bindingContext.Result = ModelBindingResult.Success(value);
        }
    }
}
