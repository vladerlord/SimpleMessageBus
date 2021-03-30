namespace SimpleMessageBus
{
    public class TcpMessageBusBandwidth
    {
        public int ReadBytes;
        public int ReadMessages;
        public int SentBytes;
        public int SentMessages;

        public void Reset()
        {
            ReadBytes = 0;
            ReadMessages = 0;
            SentBytes = 0;
            SentMessages = 0;
        }

        public void Add(TcpMessageBusBandwidth state)
        {
            ReadBytes += state.ReadBytes;
            ReadMessages += state.ReadMessages;
            SentBytes += state.SentBytes;
            SentMessages += state.SentMessages;
        }
    }
}