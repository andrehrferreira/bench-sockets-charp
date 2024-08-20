using System.Collections.Concurrent;
using System.Text;
using NanoSockets;

class UDPServer
{
    private static readonly ConcurrentDictionary<Address, (string, SemaphoreSlim)> clients = new();
    private static readonly ushort Port = 5001;
    private static readonly int ClientsToWaitFor = 100;

    private static string readyMessage = "ready";
    private static byte[] msgBuffer = Encoding.UTF8.GetBytes(readyMessage);

    static void Main(string[] args)
    {
        // Criação do socket do servidor
        Socket udpSocket = NanoSockets.UDP.Create(256 * 1024, 256 * 1024);
        Address listenAddress = new Address { port = Port };

        if (NanoSockets.UDP.SetIP(ref listenAddress, "::0") == Status.OK)
            Console.WriteLine("Address set!");

        if (NanoSockets.UDP.Bind(udpSocket, ref listenAddress) == 0)
            Console.WriteLine($"UDP server is running on udp://localhost:{Port}");

        if (NanoSockets.UDP.SetDontFragment(udpSocket) != Status.OK)
            Console.WriteLine("Don't fragment option error!");

        //if (NanoSockets.UDP.SetNonBlocking(udpSocket, true) != Status.OK)
        //    Console.WriteLine("Non-blocking option error!");

        // Loop principal do servidor
        while (true)
        {
            try
            {
                ReceiveAndHandleClient(udpSocket);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Listener error: {ex.Message}");
            }
        }
    }

    private static void ReceiveAndHandleClient(Socket udpSocket)
    {
        byte[] buffer = new byte[1024];
        Address clientEndpoint = new Address();

        int receivedBytes = NanoSockets.UDP.Receive(udpSocket, ref clientEndpoint, buffer, buffer.Length);

        if (receivedBytes > 0)
        {
            byte[] receivedData = new byte[receivedBytes];
            Array.Copy(buffer, receivedData, receivedBytes);
            _ = HandleClientAsync(udpSocket, clientEndpoint, receivedData);
        }
    }

    private static async Task HandleClientAsync(Socket udpSocket, Address clientEndpoint, byte[] buffer)
    {
        string name;
        SemaphoreSlim semaphore;

        lock (clients)
        {
            if (!clients.ContainsKey(clientEndpoint))
            {
                name = $"Client{clients.Count + 1}";
                semaphore = new SemaphoreSlim(1, 1);
                clients.TryAdd(clientEndpoint, (name, semaphore));
                Console.WriteLine($"{name} connected ({ClientsToWaitFor - clients.Count} remain)");

                if (clients.Count == ClientsToWaitFor)
                {
                    SendReadyMessage(udpSocket);
                }
            }
            else
            {
                (name, semaphore) = clients[clientEndpoint];
            }
        }

        try
        {
            string message = Encoding.UTF8.GetString(buffer);
            await BroadcastMessage(udpSocket, name, message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private static async Task BroadcastMessage(Socket udpSocket, string name, string message)
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
                    var clientKey = client.Key;
                    NanoSockets.UDP.Send(udpSocket, ref clientKey, msgBuffer, msgBuffer.Length);
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

    private static void SendReadyMessage(Socket udpSocket)
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
                        var clientKey = client.Key;
                        NanoSockets.UDP.Send(udpSocket, ref clientKey, msgBuffer, msgBuffer.Length);
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
