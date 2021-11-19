using ProtoBuf;
using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus.ExampleProducer
{
    [ProtoContract, CompatibilityLevel(CompatibilityLevel.Level300)]
    public class PersonMessage : IMessage
    {
        [ProtoMember(1)] public int Id;
        [ProtoMember(2)] public string Name;
    }
}