using ProtoBuf;
using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus.Shared
{
    [ProtoContract, CompatibilityLevel(CompatibilityLevel.Level300)]
    public class PersonMessage : IMessage
    {
        [ProtoMember(1)] public int Id;
        [ProtoMember(2)] public string Name;
    }
}