using HSMSensorDataObjects;
using HSMServer.ApiObjectsConverters;
using System.Collections.Generic;
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


        // What makes a /commands list unreadable although it is valid JSON: an element
        // that is null, or a command without a path. Null when there is nothing of the
        // kind. Both transports ask before anything is applied and answer the whole
        // request 400 (#1749): a per-path answer has no path to put such a command under,
        // and it is the request's fault, the same on every repeat, so it must not be the
        // 500 that tells a collector to send the package again.
        public static string FindMalformedCommand(IReadOnlyList<CommandRequestBase> commands)
        {
            for (var i = 0; i < commands.Count; i++)
            {
                if (commands[i] is null)
                    return $"The command at index {i} is null.";

                if (commands[i].Path is null)
                    return $"The command at index {i} has no path.";
            }

            return null;
        }


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
