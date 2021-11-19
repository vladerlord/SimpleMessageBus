using System.Threading;

namespace SimpleMessageBus
{
    public class TcpMessageBusBandwidth
    {
        public int ReadMessages;
        public int SentMessages;

        public void Reset()
        {
            Interlocked.Exchange(ref ReadMessages, 0);
            Interlocked.Exchange(ref SentMessages, 0);
        }

        public void Add(TcpMessageBusBandwidth state)
        {
            ReadMessages += state.ReadMessages;
            SentMessages += state.SentMessages;
        }
    }
}