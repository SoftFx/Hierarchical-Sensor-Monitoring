using HSMServer.ApiObjectsConverters;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.SensorApi
{
    // How the Sensor API reads the bodies of /list and /commands. One definition, used by
    // the HTTP model binders and by the collector session, so a body means the same thing
    // on both transports. JsonSerializerOptions are thread-safe once used and expensive
    // to build, hence one instance each.
    internal static class SensorApiJson
    {
        // List<SensorValueBase>: the polymorphic batch of values and bars.
        public static JsonSerializerOptions Values { get; } = BuildValues();

        // List<CommandRequestBase>: sensor registrations.
        public static JsonSerializerOptions Commands { get; } = BuildCommands();


        private static JsonSerializerOptions BuildValues()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            };

            options.Converters.Add(new SensorValueBaseDeserializationConverter());

            return options;
        }

        private static JsonSerializerOptions BuildCommands()
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            options.Converters.Add(new CommandRequestBaseDeserializationConverter());

            return options;
        }
    }
}
