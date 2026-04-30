using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace QuantumSqueezingUI
{
    public class PipeClient : IDisposable
    {
        private NamedPipeClientStream pipeClient;
        private StreamReader reader;
        private StreamWriter writer;
        private SemaphoreSlim pipeLock = new SemaphoreSlim(1, 1);

        public bool IsConnected => pipeClient?.IsConnected ?? false;

        public PipeClient()
        {
            pipeClient = new NamedPipeClientStream(".", "QuantumDAQPipe", PipeDirection.InOut, PipeOptions.Asynchronous);


        }

        public async Task ConnectAsync()
        {
            if (!IsConnected)
            {
                await pipeClient.ConnectAsync(1000); // 5 second timeout
                reader = new StreamReader(pipeClient, Encoding.UTF8);
                writer = new StreamWriter(pipeClient, Encoding.UTF8) { AutoFlush = true };
            }
        }

        public async Task<string> SendCommandAsync(string command)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Pipe is not connected.");

            byte[] messageBytes = Encoding.UTF8.GetBytes(command);

            await pipeLock.WaitAsync();
            try
            {
                // Send length first
                byte[] lengthBytes = BitConverter.GetBytes(messageBytes.Length);
                await pipeClient.WriteAsync(lengthBytes, 0, lengthBytes.Length);

                // Then send actual message
                await pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length);
                await pipeClient.FlushAsync();

                byte[] responseLengthBytes = new byte[4];
                await ReadExactAsync(responseLengthBytes, 0, responseLengthBytes.Length);
                int responseLength = BitConverter.ToInt32(responseLengthBytes, 0);
                if (responseLength < 0 || responseLength > 1024 * 1024)
                    throw new InvalidOperationException($"Invalid DAQ pipe response length: {responseLength}");

                byte[] responseBytes = new byte[responseLength];
                await ReadExactAsync(responseBytes, 0, responseLength);
                return Encoding.UTF8.GetString(responseBytes);
            }
            finally
            {
                pipeLock.Release();
            }
        }

        private async Task ReadExactAsync(byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await pipeClient.ReadAsync(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                    throw new IOException("DAQ pipe closed before a complete response was received.");

                totalRead += read;
            }
        }

        public void Dispose()
        {
            try { reader?.Dispose(); } catch { }
            try { writer?.Dispose(); } catch { }
            try { pipeClient?.Dispose(); } catch { }
            try { pipeLock?.Dispose(); } catch { }
        }
    }
}
