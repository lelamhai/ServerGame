using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class Player
{
    public string Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public TcpClient Client { get; set; }
    public NetworkStream Stream { get; set; }
}

public class GameServer
{
    private TcpListener server;
    private Dictionary<string, Player> players = new Dictionary<string, Player>();
    private bool isRunning = true;

    public void Start(int port)
    {
        server = new TcpListener(IPAddress.Any, port);
        server.Start();
        Console.WriteLine($"Server started on port {port}");

        Thread acceptThread = new Thread(new ThreadStart(AcceptClients));
        acceptThread.Start();
    }

    private void AcceptClients()
    {
        while (isRunning)
        {
            try
            {
                TcpClient client = server.AcceptTcpClient();
                Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClient));
                clientThread.Start(client);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accepting client: {ex.Message}");
            }
        }
    }

    private void HandleClient(object obj)
    {
        TcpClient client = (TcpClient)obj;
        NetworkStream stream = client.GetStream();
        string playerId = Guid.NewGuid().ToString();

        try
        {
            Player newPlayer = new Player
            {
                Id = playerId,
                X = 0,
                Y = 0,
                Z = 0,
                Client = client,
                Stream = stream
            };

            lock (players)
            {
                players.Add(playerId, newPlayer);
            }

            Console.WriteLine($"Player {playerId} connected. Total players: {players.Count}");

            // Send ID to new client
            byte[] idData = Encoding.ASCII.GetBytes($"ID:{playerId}\n");
            stream.Write(idData, 0, idData.Length);

            // Send all existing players to new client
            SendAllPlayersToNewClient(newPlayer);

            // Notify other players about new player
            NotifyOtherPlayers(newPlayer);

            // Handle client messages
            byte[] buffer = new byte[1024];
            int bytesRead;


            while (isRunning && client.Connected)
            {
                try
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                        break;

                    string data = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    string[] messages = data.Split('\n');

                    foreach (string message in messages)
                    {
                        if (!string.IsNullOrEmpty(message))
                        {
                            ProcessClientData(newPlayer, message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading from client {playerId}: {ex.Message}");
                    break;
                }
            }
        }
        finally
        {
            lock (players)
            {
                players.Remove(playerId);
            }
            Console.WriteLine($"Player {playerId} disconnected. Total players: {players.Count}");
            client.Close();
            NotifyPlayerDisconnected(playerId);
        }
    }

    private void SendAllPlayersToNewClient(Player newPlayer)
    {
        lock (players)
        {
            // Send count first
            string countMsg = $"COUNT:{players.Count - 1}\n";
            byte[] countData = Encoding.ASCII.GetBytes(countMsg);
            newPlayer.Stream.Write(countData, 0, countData.Length);

            // Send each player
            foreach (var player in players.Values)
            {
                if (player.Id != newPlayer.Id)
                {
                    string message = $"NEW:{player.Id}:{player.X}:{player.Y}:{player.Z}\n";
                    byte[] data = Encoding.ASCII.GetBytes(message);
                    newPlayer.Stream.Write(data, 0, data.Length);
                    Thread.Sleep(10); // Small delay to prevent flooding
                }
            }

            // Send done signal
            string doneMsg = "DONE\n";
            byte[] doneData = Encoding.ASCII.GetBytes(doneMsg);
            newPlayer.Stream.Write(doneData, 0, doneData.Length);
        }
    }

    private void NotifyOtherPlayers(Player newPlayer)
    {
        string message = $"NEW:{newPlayer.Id}:{newPlayer.X}:{newPlayer.Y}:{newPlayer.Z}\n";
        byte[] data = Encoding.ASCII.GetBytes(message);

        lock (players)
        {
            foreach (var player in players.Values)
            {
                if (player.Id != newPlayer.Id && player.Client.Connected)
                {
                    try
                    {
                        player.Stream.Write(data, 0, data.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error notifying player {player.Id}: {ex.Message}");
                    }
                }
            }
        }
    }

    private void NotifyPlayerDisconnected(string playerId)
    {
        string message = $"DEL:{playerId}\n";
        byte[] data = Encoding.ASCII.GetBytes(message);

        lock (players)
        {
            foreach (var player in players.Values)
            {
                if (player.Client.Connected)
                {
                    try
                    {
                        player.Stream.Write(data, 0, data.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error notifying player {player.Id}: {ex.Message}");
                    }
                }
            }
        }
    }

    private void ProcessClientData(Player player, string data)
    {
        string[] parts = data.Split(':');
        if (parts.Length < 4) return;

        string command = parts[0];
        if (command == "POS")
        {
            if (float.TryParse(parts[1], out float x) &&
                float.TryParse(parts[2], out float y) &&
                float.TryParse(parts[3], out float z))
            {
                player.X = x;
                player.Y = y;
                player.Z = z;

                BroadcastPosition(player.Id, x, y, z);
            }
        }
    }

    private void BroadcastPosition(string playerId, float x, float y, float z)
    {
        string message = $"POS:{playerId}:{x}:{y}:{z}\n";
        byte[] data = Encoding.ASCII.GetBytes(message);

        lock (players)
        {
            foreach (var player in players.Values)
            {
                if (player.Id != playerId && player.Client.Connected)
                {
                    try
                    {
                        player.Stream.Write(data, 0, data.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error broadcasting to {player.Id}: {ex.Message}");
                    }
                }
            }
        }
    }

    public void Stop()
    {
        isRunning = false;
        server.Stop();
    }
}

class Program
{
    static void Main(string[] args)
    {
        GameServer server = new GameServer();
        server.Start(8888);

        Console.WriteLine("Server running. Press any key to stop...");
        Console.ReadKey();

        server.Stop();
    }
}