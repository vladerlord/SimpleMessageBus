using System;
using System.IO;
using ProtoBuf;
using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus
{
    public static class MessageSerializerExtension
    {
        public static byte[] Serialize(this IMessage content)
        {
            using var ms = new MemoryStream();

            Serializer.Serialize(ms, content);

            return ms.ToArray();
        }

        public static T Deserialize<T>(this byte[] content)
        {
            using var ms = new MemoryStream(content) {Position = 0};
            
            return Serializer.Deserialize<T>(ms);
        }

        public static ISerializable Deserialize(this byte[] content, Type type)
        {
            using var ms = new MemoryStream(content);

            return (ISerializable) Serializer.NonGeneric.Deserialize(type, ms);
        }
    }
}