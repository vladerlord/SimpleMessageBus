using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus.Shared
{
    public class ExampleMessagesIdsBinding : MessagesIdsBinding
    {
        public ExampleMessagesIdsBinding()
        {
            MessageBinding.Add(typeof(PersonMessage), 1);
        }
    }
}