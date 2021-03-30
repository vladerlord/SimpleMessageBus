using ProtoBuf;

namespace SimpleMessageBus.ExampleAbstractions
{
    [ProtoContract, CompatibilityLevel(CompatibilityLevel.Level300)]
    public struct PersonMessage : IMessage
    {
        [ProtoMember(1)] public long MessageId { get; set; }
        [ProtoMember(2)] public int Id { get; set; }
        [ProtoMember(3)] public string Name { get; set; }

        public object Clone()
        {
            return new PersonMessage
            {
                MessageId = MessageId
            };
        }
    }
}