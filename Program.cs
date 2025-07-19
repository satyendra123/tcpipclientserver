﻿using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MySql.Data.MySqlClient;

class TcpQrClient
{
    static TcpClient client;
    static NetworkStream stream;
    static string serverIp;
    static int serverPort;
    static string connString;

    static string configPath = "config.txt";
    static string logPath = "connectivitylog.txt";

    static void Main()
    {
        LoadConfig();

        while (true)
        {
            try
            {
                Log($"Trying to connect to {serverIp}:{serverPort}...");
                client = new TcpClient();
                client.Connect(serverIp, serverPort);

                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                uint dummy = 0;
                byte[] inOptionValues = new byte[12];
                BitConverter.GetBytes((uint)1).CopyTo(inOptionValues, 0);
                BitConverter.GetBytes((uint)5000).CopyTo(inOptionValues, 4);
                BitConverter.GetBytes((uint)1000).CopyTo(inOptionValues, 8);

                client.Client.IOControl(IOControlCode.KeepAliveValues, inOptionValues, null);

                Log("Connected to Arduino");
                Console.WriteLine("Connected to Arduino");

                stream = client.GetStream();

                while (client.Connected)
                {
                    if (CheckBoomsigAndTriggerRelay())
                    {
                        Log("entryboom was Y, command sent to Arduino and updated to N.");
                    }
                    Thread.Sleep(1000);
                }
            }
            catch (SocketException ex)
            {
                Log("Socket Error: " + ex.Message);
                Console.WriteLine("Socket Error: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log("Unexpected Error: " + ex.Message);
                Console.WriteLine("Unexpected Error: " + ex.Message);
            }
            finally
            {
                stream?.Close();
                client?.Close();
                Log("Disconnected from Arduino. Retrying in 3 seconds...");
                Console.WriteLine("Retrying connection in 3 seconds...");
                Thread.Sleep(3000);
            }
        }
    }

    static void LoadConfig()
    {
        try
        {
            Dictionary<string, string> config = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(configPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("#") || !line.Contains("=")) continue;
                var parts = line.Split('=', 2);
                config[parts[0].Trim()] = parts[1].Trim();
            }

            serverIp = config["server_ip"];
            serverPort = int.Parse(config["server_port"]);
            connString = config["db_connection"];
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error reading config.txt: " + ex.Message);
            Log("Error reading config.txt: " + ex.Message);
            Environment.Exit(1);
        }
    }

    static bool CheckBoomsigAndTriggerRelay()
    {
        try
        {
            using (MySqlConnection conn = new MySqlConnection(connString))
            {
                conn.Open();
                string query = "SELECT entryboom FROM boomsig WHERE id = 1";
                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    var result = cmd.ExecuteScalar();
                    if (result != null && result.ToString().Trim().Equals("Y", StringComparison.OrdinalIgnoreCase))
                    {
                        Log("entryboom is Y, sending |OPENEN% command to Arduino");
                        byte[] openCmd = Encoding.ASCII.GetBytes("|OPENEN%");
                        stream.Write(openCmd, 0, openCmd.Length);

                        string updateQuery = "UPDATE boomsig SET entryboom = 'N' WHERE id = 1";
                        using (MySqlCommand updateCmd = new MySqlCommand(updateQuery, conn))
                        {
                            updateCmd.ExecuteNonQuery();
                            Log("Updated entryboom to N in boomsig table.");
                        }

                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log("Database Error: " + ex.Message);
        }

        return false;
    }

    static void Log(string message)
    {
        try
        {
            string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            File.AppendAllText(logPath, logMessage + Environment.NewLine);
        }
        catch
        {
            // Silent catch if logging fails
        }
    }
}
