namespace SimpleMessageBus.Server
{
    public static class ServerConfig
    {
        public static ushort MessageAckTimeout;

        public static void LoadOptions(ServerOptions options)
        {
            MessageAckTimeout = options.MessageAckTimeout;
        }
    }
    
    public class ServerOptions
    {
        public ushort MessageAckTimeout { init; get; } = 6;
    }
}