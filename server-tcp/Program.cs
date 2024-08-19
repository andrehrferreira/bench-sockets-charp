using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

class TCPServer
{
    private static readonly ConcurrentDictionary<TcpClient, (string, SemaphoreSlim)> clients = new();
    private static readonly int Port = 4001;
    private static readonly int ClientsToWaitFor = 100;

    private static string readyMessage = "ready";
    private static byte[] msgBuffer = Encoding.UTF8.GetBytes(readyMessage);
    private static ArraySegment<byte> segment = new ArraySegment<byte>(msgBuffer);

    static async Task Main(string[] args)
    {
        TcpListener listener = new(IPAddress.Any, Port);
        listener.Start();
        Console.WriteLine($"TCP server is running on tcp://localhost:{Port}");

        while (true)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = HandleClientAsync(client);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Listener error: {ex.Message}");
            }
        }
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        string name = $"Client{clients.Count + 1}";
        SemaphoreSlim semaphore = new(1, 1);
        clients.TryAdd(client, (name, semaphore));

        Console.WriteLine($"{name} connected ({ClientsToWaitFor - clients.Count} remain)");

        if (clients.Count == ClientsToWaitFor)
        {
            SendReadyMessage();
        }

        try
        {
            var buffer = new byte[1024];
            NetworkStream stream = client.GetStream();

            while (client.Connected)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

                if (bytesRead == 0)
                    break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                await BroadcastMessage(name, message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            client.Close();
            clients.TryRemove(client, out _);
        }
    }

    private static async Task BroadcastMessage(string name, string message)
    {
        string msg = $"{name}: {message}";
        byte[] msgBuffer = Encoding.UTF8.GetBytes(msg);

        var clientsCopy = clients.ToArray();

        var tasks = clientsCopy
            .Where(c => c.Key.Connected)
            .Select(async client =>
            {
                var semaphore = client.Value.Item2;
                await semaphore.WaitAsync();
                try
                {
                    NetworkStream stream = client.Key.GetStream();
                    await stream.WriteAsync(msgBuffer, 0, msgBuffer.Length).ConfigureAwait(false);
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

    private static void SendReadyMessage()
    {
        Task.Delay(100).ContinueWith(async _ =>
        {
            var clientsCopy = clients.ToArray();

            var tasks = clientsCopy
                .Where(c => c.Key.Connected)
                .Select(async client =>
                {
                    var semaphore = client.Value.Item2;
                    await semaphore.WaitAsync();

                    try
                    {
                        NetworkStream stream = client.Key.GetStream();
                        await stream.WriteAsync(msgBuffer, 0, msgBuffer.Length).ConfigureAwait(false);
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
