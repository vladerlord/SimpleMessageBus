namespace SimpleMessageBus.Abstractions
{
    public interface IMessageHandler<in T> where T : ISerializable
    {
        public void Handle(T message);
    }
}