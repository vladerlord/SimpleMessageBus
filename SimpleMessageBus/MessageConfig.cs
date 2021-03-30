namespace SimpleMessageBus
{
    public static class MessageConfig
    {
        public static byte Delimiter { get; } = (byte) '\0';
        public static byte DelimiterLength { get; } = 1;
    }
}