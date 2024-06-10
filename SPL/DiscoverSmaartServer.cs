using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

public class DiscoverSmaartServer
{
    public static async Task<(string, int)> DiscoverSmaartServerAsync(uint productSignature, int serverPort, Action<string> statusCallback)
    {
        try
        {
            statusCallback($"Discovering product with signature 0x{productSignature:X8} on port {serverPort}...");
            var serverInfo = await DiscoverProductAsync(productSignature, serverPort, statusCallback);
            if (serverInfo != (null, 0))
            {
                return serverInfo;
            }
            else
            {
                throw new Exception("Failed to discover Smaart server.");
            }
        }
        catch (Exception ex)
        {
            statusCallback($"Error: {ex.Message}");
            return (null, 0);
        }
    }

    private static async Task<(string, int)> DiscoverProductAsync(uint signature, int discoveryPort, Action<string> statusCallback)
    {
        byte[] query = new byte[8];
        Buffer.BlockCopy(BitConverter.GetBytes(signature), 0, query, 0, 4);

        // TCP port to listen for responses (chosen randomly)
        ushort responsePort = (ushort)new Random().Next(1024, 65535);
        Buffer.BlockCopy(BitConverter.GetBytes(responsePort), 0, query, 4, 2);

        // Print the query structure
        /*
        statusCallback("Query structure:");
        foreach (var b in query)
        {
            statusCallback($"{b:X2} ");
        }
        statusCallback("");
        */

        IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, responsePort);
        UdpClient udpClient = new UdpClient();

        TcpListener tcpListener = null;

        try
        {
            IPEndPoint broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
            udpClient.Send(query, query.Length, broadcastEndPoint);
            //statusCallback($"Sent discovery query to port {discoveryPort}.");
            statusCallback("Searching for server...");

            // Set up TCP listener
            tcpListener = new TcpListener(localEndPoint);
            tcpListener.Start();
            //statusCallback($"Listening for TCP responses on port {responsePort}.");

            // Asynchronously accept TCP connection with timeout
            var acceptTask = tcpListener.AcceptTcpClientAsync();
            var delayTask = Task.Delay(5000); // Timeout after 5 seconds

            // Wait for either task to complete
            var completedTask = await Task.WhenAny(acceptTask, delayTask);

            if (completedTask == acceptTask)
            {
                using (var tcpClient = await acceptTask)
                using (var stream = tcpClient.GetStream())
                {
                    byte[] response = new byte[8];
                    await stream.ReadAsync(response, 0, response.Length);

                    uint responseSignature = BitConverter.ToUInt32(response, 0);
                    ushort serverPort = BitConverter.ToUInt16(response, 4);

                    if (responseSignature == signature)
                    {
                        statusCallback($"Found server at {((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address}:{serverPort}.");
                        return (((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address.ToString(), serverPort);
                    }
                }
            }
            else
            {
                throw new TimeoutException("TCP response timed out.");
            }
        }
        catch (TimeoutException ex)
        {
            statusCallback($"Error: {ex.Message}");
        }
        catch (SocketException ex)
        {
            if (ex.SocketErrorCode == SocketError.TimedOut)
            {
                statusCallback("Discovery timed out.");
            }
            else
            {
                statusCallback($"Socket Error: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            statusCallback($"General Error: {ex.Message}");
        }
        finally
        {
            udpClient.Close();
            if (tcpListener != null)
            {
                tcpListener.Stop();
            }
        }

        return (null, 0); // Return a tuple with null and 0 if discovery failed
    }
}
