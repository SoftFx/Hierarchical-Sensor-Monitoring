using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace HSMDataCollector.Serialization
{
    public static class Serializer
    {
        private static readonly Dictionary<Type, DataContractJsonSerializer> _typeToSerializer;

        static Serializer()
        {
            _typeToSerializer = new Dictionary<Type, DataContractJsonSerializer>();
        }

        public static string Serialize<T>(T obj)
        {
            string result;
            var serializer = GetSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, obj);
                result = Encoding.UTF8.GetString(stream.ToArray());
            }

            return result;
        }

        private static DataContractJsonSerializer GetSerializer(Type type)
        {
            if (_typeToSerializer.ContainsKey(type))
            {
                return _typeToSerializer[type];
            }

            DataContractJsonSerializer serializer = new DataContractJsonSerializer(type, new DataContractJsonSerializerSettings() {UseSimpleDictionaryFormat = true});
            _typeToSerializer[type] = serializer;
            return serializer;
        }
    }
}
