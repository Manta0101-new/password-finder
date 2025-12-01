// NetworkHelper.cs
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PasswordFileFinderGUI
{
    public static class NetworkHelper
    {
        // Protocol Helper: Read Length-Prefixed String
        public static async Task<string> ReadMessageAsync(SslStream ssl, CancellationToken token)
        {
            byte[] lengthBuffer = new byte[4];
            int read = await ssl.ReadAsync(lengthBuffer, 0, 4, token);
            if (read == 0) return string.Empty;

            int length = BitConverter.ToInt32(lengthBuffer, 0);

            byte[] buffer = new byte[length];
            int bytesRead = 0;
            while (bytesRead < length)
            {
                bytesRead += await ssl.ReadAsync(buffer, bytesRead, length - bytesRead, token);
            }
            return Encoding.UTF8.GetString(buffer);
        }

        // Protocol Helper: Write Length-Prefixed String
        public static async Task WriteMessageAsync(SslStream ssl, string message, CancellationToken token)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            byte[] length = BitConverter.GetBytes(data.Length);
            await ssl.WriteAsync(length, 0, length.Length, token);
            await ssl.WriteAsync(data, 0, data.Length, token);
        }
    }
}