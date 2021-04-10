using System.Text;

namespace SimpleMessageBus.Utils
{
    public static class StringExtension
    {
        public static byte[] ToBytes(this string str)
        {
            return Encoding.ASCII.GetBytes(str);
        }

        public static string GetString(this byte[] byteArray)
        {
            return Encoding.ASCII.GetString(byteArray);
        }
    }
}