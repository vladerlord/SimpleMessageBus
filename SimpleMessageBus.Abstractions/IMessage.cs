namespace SimpleMessageBus.Abstractions
{
    public enum MessageType
    {
        Heartbeat = '0',
        Message = '1',
        Subscribe = '2',
        Ack = '3',
        Connect = '4'
    }

    public interface IMessage
    {
    }
}