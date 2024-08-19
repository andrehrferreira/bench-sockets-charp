using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

class UDPServer
{
    private static readonly ConcurrentDictionary<IPEndPoint, (string, SemaphoreSlim)> clients = new();
    private static readonly int Port = 5001;
    private static readonly int ClientsToWaitFor = 100;

    private static string readyMessage = "ready";
    private static byte[] msgBuffer = Encoding.UTF8.GetBytes(readyMessage);

    static async Task Main(string[] args)
    {
        UdpClient udpClient = new UdpClient(Port);
        Console.WriteLine($"UDP server is running on udp://localhost:{Port}");

        while (true)
        {
            try
            {
                var result = await udpClient.ReceiveAsync();
                _ = HandleClientAsync(udpClient, result.RemoteEndPoint, result.Buffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Listener error: {ex.Message}");
            }
        }
    }

    private static async Task HandleClientAsync(UdpClient udpClient, IPEndPoint clientEndPoint, byte[] buffer)
    {
        string name;
        SemaphoreSlim semaphore;

        // Thread-safe client registration
        lock (clients)
        {
            if (!clients.ContainsKey(clientEndPoint))
            {
                name = $"Client{clients.Count + 1}";
                semaphore = new SemaphoreSlim(1, 1);
                clients.TryAdd(clientEndPoint, (name, semaphore));
                Console.WriteLine($"{name} connected ({ClientsToWaitFor - clients.Count} remain)");

                if (clients.Count == ClientsToWaitFor)
                {
                    SendReadyMessage(udpClient);
                }
            }
            else
            {
                // Retrieve existing client info
                (name, semaphore) = clients[clientEndPoint];
            }
        }

        try
        {
            string message = Encoding.UTF8.GetString(buffer);
            await BroadcastMessage(udpClient, name, message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private static async Task BroadcastMessage(UdpClient udpClient, string name, string message)
    {
        string msg = $"{name}: {message}";
        byte[] msgBuffer = Encoding.UTF8.GetBytes(msg);

        var clientsCopy = clients.ToArray();

        var tasks = clientsCopy
            .Select(async client =>
            {
                var semaphore = client.Value.Item2;

                await semaphore.WaitAsync();
                try
                {
                    await udpClient.SendAsync(msgBuffer, msgBuffer.Length, client.Key).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending message: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

        await Task.WhenAll(tasks);
    }

    private static void SendReadyMessage(UdpClient udpClient)
    {
        Task.Delay(100).ContinueWith(async _ =>
        {
            var clientsCopy = clients.ToArray();

            var tasks = clientsCopy
                .Select(async client =>
                {
                    var semaphore = client.Value.Item2;

                    await semaphore.WaitAsync();
                    try
                    {
                        await udpClient.SendAsync(msgBuffer, msgBuffer.Length, client.Key).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error sending ready message: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

            await Task.WhenAll(tasks);
        });
    }
}
