using SimpleMessageBus.Abstractions;

namespace SimpleMessageBus.ExampleShared
{
    public class ExampleMessagesIdsBinding : MessagesIdsBinding
    {
        public ExampleMessagesIdsBinding()
        {
            MessageBinding.Add(typeof(PersonMessage), 1);
        }
    }
}