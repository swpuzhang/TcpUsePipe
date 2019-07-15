using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TcpEcho
{
    class Program
    {
        private static async Task ProcessLinesAsync(Socket socket)
        {
            
            var pipe = new Pipe();
            Task writing = FillPipeAsync(socket, pipe.Writer);
            Task reading = ReadPipeAsync(socket, pipe.Reader);

            await Task.WhenAll(reading, writing);

        }


        private static void ProcessLine(Socket socket, in ReadOnlySequence<byte> buffer)
        {

      
            foreach (var segment in buffer)
            {

                Console.Write(Encoding.UTF8.GetString(segment));
            }
            Console.WriteLine();

        }


        private static async Task FillPipeAsync(Socket socket, PipeWriter writer)
        {
            const int minimumBufferSize = 512;

            while (true)
            {
                try
                {
                    // Request a minimum of 512 bytes from the PipeWriter
                    Memory<byte> memory = writer.GetMemory(minimumBufferSize);

                    int bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    // Tell the PipeWriter how much was read
                    writer.Advance(bytesRead);
                }
                catch
                {
                    break;
                }

                // Make the data available to the PipeReader
                FlushResult result = await writer.FlushAsync();

                if (result.IsCompleted)
                {
                    break;
                }
            }

            // Signal to the reader that we're done writing
            writer.Complete();
        }

        private static async Task ReadPipeAsync(Socket socket, PipeReader reader)
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync();

                ReadOnlySequence<byte> buffer = result.Buffer;
                
                    
                   
               ProcessLine(socket, buffer);
                
                // We sliced the buffer until no more data could be processed
                // Tell the PipeReader how much we consumed and how much we left to process
                reader.AdvanceTo(buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }

            reader.Complete();
        }

       

        static async Task Main(string[] args)
        {
            var messageSize = args.FirstOrDefault();

            var clientSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);

            Console.WriteLine("Connecting to port 8087");

            clientSocket.Connect(new IPEndPoint(IPAddress.Loopback, 8007));

            if (messageSize == null)
            {
                var buffer = new byte[1];
                _ = ProcessLinesAsync(clientSocket);
                while (true)
                {
                    
                    buffer[0] = (byte)Console.Read();
                    _ = clientSocket.SendAsync(new ArraySegment<byte>(buffer, 0, 1), SocketFlags.None);
                }
            }
            else
            {
                var count = int.Parse(messageSize);
                var buffer = Encoding.ASCII.GetBytes(new string('a', count) + Environment.NewLine);
                var stopwatch = new Stopwatch();
                stopwatch.Start();
                for (int i = 0; i < 1_000_000; i++)
                {
                    await clientSocket.SendAsync(new ArraySegment<byte>(buffer), SocketFlags.None);
                }
                stopwatch.Stop();

                Console.WriteLine($"Elapsed {stopwatch.Elapsed.TotalSeconds:F} sec.");
            }
        }
    }

    internal static class Extensions
    {
        public static Task<int> ReceiveAsync(this Socket socket, Memory<byte> memory, SocketFlags socketFlags)
        {
            var arraySegment = GetArray(memory);
            return SocketTaskExtensions.ReceiveAsync(socket, arraySegment, socketFlags);
        }

        public static string GetString(this Encoding encoding, ReadOnlyMemory<byte> memory)
        {
            var arraySegment = GetArray(memory);
            return encoding.GetString(arraySegment.Array, arraySegment.Offset, arraySegment.Count);
        }

        private static ArraySegment<byte> GetArray(Memory<byte> memory)
        {
            return GetArray((ReadOnlyMemory<byte>)memory);
        }

        private static ArraySegment<byte> GetArray(ReadOnlyMemory<byte> memory)
        {
            if (!MemoryMarshal.TryGetArray(memory, out var result))
            {
                throw new InvalidOperationException("Buffer backed by array was expected");
            }

            return result;
        }

    }
}
