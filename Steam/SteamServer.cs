/*
	All files containing this header is released under the GPL 3.0 license.

	Initial author: (https://github.com/)Convery
	Started: 2014-10-28
	Notes:
        Async TCP server.
        Settings can be imported from an XML file.
        TODO: Link the packet handler.
*/

using System;
using System.Xml;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;

namespace SteamServer
{
    class SteamServer
    {
        // Node info.
        public static Dictionary<UInt64, IPAddress> KnownNodes;
        public static UInt64 NodeID;        // [Public] This nodes identifier on the network.
        public static UInt64 SessionID;     // [Private] Identity verification.
        public static Boolean isConnected;  // [Public] Is connected to other nodes.
        public static Boolean ShouldConnect;// [Private] Should connect to the master and other nodes.
        public static Boolean Anonymous;    // [Public] Node does not authenticate the client.

        // Server info.
        public  static UInt16 Port; // Listen port.
        private static UInt16 Limit = 250; // Backlog.
        private static ManualResetEvent MRE = new ManualResetEvent(false);
        public  static Dictionary<UInt32, SteamClient> Clients = new Dictionary<UInt32, SteamClient>(); // Client map.
        public static ServiceManager ServiceRouter = new ServiceManager(); // Handles all incomming packets.

        // General methods.
        public static void InitServer()
        {
            new Thread(new ThreadStart(CleanupThread)).Start();

            if (!File.Exists("NodeSettings.xml"))
            {
                NodeID = 0;
                SessionID = 0;
                isConnected = false;
                ShouldConnect = false;
                Anonymous = true;
                Port = 42042;
            }
            else
            {
                XmlTextReader xReader = new XmlTextReader("NodeSettings.xml");
                String LastElement = null;

                while (xReader.Read())
                {
                    switch (xReader.NodeType)
                    {
                        case XmlNodeType.Element:
                            LastElement = xReader.Name;
                            break;
                        case XmlNodeType.Text:
                            if (LastElement != null) // This should never happen.
                            {
                                switch (LastElement)
                                {
                                    // Node info.
                                    case "NodeID":
                                        NodeID = UInt64.Parse(xReader.Value);
                                        break;
                                    case "ShouldConnect":
                                        ShouldConnect = Boolean.Parse(xReader.Value);
                                        break;
                                    case "Anonymous":
                                        Anonymous = Boolean.Parse(xReader.Value);
                                        break;

                                    // Server info.
                                    case "Port":
                                        Port = UInt16.Parse(xReader.Value);
                                        break;
                                }
                            }
                            break;
                        case XmlNodeType.EndElement:
                            LastElement = null;
                            break;
                    }
                }
            }
        }
        public static void Send(UInt32 ID, Byte[] Data)
        {
            Byte[] ResultBuffer = new Byte[1];

            if (!isClientConnected(ID))
            {
                RemoveClient(ID);
                return;
            }

            try
            {
                ResultBuffer = new Byte[Data.Length + 4];
                Array.Copy(BitConverter.GetBytes(Data.Length), 0, ResultBuffer, 0, 4);
                Array.Copy(Data, 0, ResultBuffer, 4, Data.Length);

                lock (Clients)
                {
                    Clients[ID].ClientSocket.BeginSend(ResultBuffer, 0, ResultBuffer.Length, SocketFlags.None, new AsyncCallback(SendCallback), Clients[ID]);
                }
            }
            catch (Exception e)
            {
                Log.Warning(e.Message);

                if (e is SocketException)
                {
                    RemoveClient(ID);
                }
            }
        }
        public static void StartListening()
        {
            IPAddress IP = IPAddress.Any;
            IPEndPoint Socket = new IPEndPoint(IP, Port);

            try
            {
                using (Socket Listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    Listener.Bind(Socket);
                    Listener.Listen(Limit);

                    Log.Debug("Waiting for connection!");
                    Listener.BeginAccept(new AsyncCallback(ConnectCallback), Listener);
                    
                }
            }
            catch (Exception e)
            {
                Log.Warning(e.InnerException.ToString());
            }
        }

        // Client methods.
        public static UInt32 FindClient(UInt64 XUID)
        {
            try
            {
                var Query = (from Client in Clients
                             where (Client.Value.XUID == XUID)
                             select Client.Key).ToList();

                if (Query.Count >= 1)
                    return Query[0];
            }
            catch (Exception e)
            {
            }
            return 0xFFFFFFFF;
        }
        public static UInt32 FindClient(Byte[] Username)
        {
            try
            {
                var Query = (from Client in Clients
                             where (SteamCrypto.fnv1_hash(Client.Value.Username) == SteamCrypto.fnv1_hash(Username))
                             select Client.Key).ToList();

                if (Query.Count >= 1)
                    return Query[0];
            }
            catch (Exception e)
            {
            }
            return 0xFFFFFFFF;
        }
        public static bool isClientConnected(UInt32 ID)
        {
            if (Clients.ContainsKey(ID))
                return true;

            try
            {
                lock(Clients)
                    return !(Clients[ID].ClientSocket.Poll(1, SelectMode.SelectRead) && Clients[ID].ClientSocket.Available == 0);
            }
            catch (Exception e)
            {
                Log.Warning(e.Message);
                return true;
            }
        }
        public static void RemoveClient(UInt32 ID)
        {
            if (!Clients.ContainsKey(ID))
                return;

            lock (Clients)
            {
                try
                {
                    // Free the memory.
                    Array.Clear(Clients[ID].Buffer, 0, Clients[ID].Buffer.Length);
                    Clients[ID].Buffer = null;

                    // Send a proper disconnect.
                    Clients[ID].ClientSocket.BeginDisconnect(false, new AsyncCallback(DisconnectCallback), Clients[ID]);

                    // And get impatient.
                    Clients[ID].ClientSocket.Shutdown(SocketShutdown.Both);
                    Clients[ID].ClientSocket.Close(1000);
                }
                catch (Exception e)
                {
                    Log.Warning(e.Message);
                }
                finally
                {
                    // Remove the client from the map.
                    Clients.Remove(ID);
                }
            }
        }

        // Async callbacks.
        private static void ConnectCallback(IAsyncResult result)
        {
            SteamClient Client = new SteamClient();
            
            try
            {
                Socket listener = (Socket)result.AsyncState;
                Client.ClientSocket = listener.EndAccept(result);

                Log.Info(String.Format("Client connected from {0}", Client.ClientSocket.RemoteEndPoint.ToString()));

                lock (Clients)
                {
                    Client.ClientID = !Clients.Any() ? 0 : Clients.Keys.Max() + 1;
                    Clients.Add(Client.ClientID, Client);
                }

                Client.ClientSocket.BeginReceive(Client.Buffer, 0, SteamClient.BufferSize, SocketFlags.None, new AsyncCallback(ReceiveCallback), Client);
                
                // Our Accept function ended, we can now Accept more clients yay!
                listener.BeginAccept(new AsyncCallback(ConnectCallback), listener); 
            }
            catch (Exception e)
            {
                Log.Error(e.GetType().Name);
                Log.Error(e.Message);
                Log.Error(e.StackTrace);
            }
        }
        private static void DisconnectCallback(IAsyncResult result)
        {
            SteamClient Client = (SteamClient)result.AsyncState;

            try
            {
                Client.ClientSocket.Close();
                Client.ClientSocket.Dispose();

            }
            catch (Exception e)
            {
                Client.ClientSocket.EndDisconnect(result);
                Log.Warning(e.ToString());
            }
        }
        private static void SendCallback(IAsyncResult result)
        {
            SteamClient Client = (SteamClient)result.AsyncState;
            try
            {
                Client.ClientSocket.EndSend(result);
            }
            catch (Exception e)
            {
                if (e is SocketException)
                {
                    RemoveClient(Client.ClientID);
                    return;
                }

                Log.Warning(e.ToString());
            }
        }
        private static void ReceiveCallback(IAsyncResult result)
        {
            SteamClient Client = (SteamClient)result.AsyncState;

            //Check for forced closes
            try
            {
                Client.ClientSocket.EndReceive(result);
            }
            catch(Exception e)
            {
                Log.Warning("Connection Error");
                RemoveClient(Client.ClientID);
                return;
            }

            try
            {
                // Update the heartbeat.
                Client.LastPacket = DateTime.Now;

                // We handle the packet and send a response here.
                ServiceRouter.HandlePacket(Client);

                // Continue.
                Client.ClientSocket.BeginReceive(Client.Buffer, 0, SteamClient.BufferSize, SocketFlags.None, new AsyncCallback(ReceiveCallback), Client);
                
            }
            catch (Exception e)
            {
                Log.Warning(e.Message);
                RemoveClient(Client.ClientID);
            }
        }

        // Free the sockets if a client drops.
        private static void CleanupThread()
        {
            try
            {
                List<SteamClient> ToPurge = new List<SteamClient>();

                while (true)
                {
                    Thread.Sleep(1000);

                    lock (Clients)
                    {
                        foreach (KeyValuePair<UInt32, SteamClient> Client in Clients)
                        {
                            if (Client.Value == null)
                            {
                                ToPurge.Add(Client.Value);
                                continue;
                            }

                            if (!isClientConnected(Client.Key))
                            {
                                ToPurge.Add(Client.Value);
                                continue;
                            }

                            if (Client.Value.LastPacket != null)
                            {
                                if ((DateTime.Now - Client.Value.LastPacket).TotalSeconds > 15)
                                {
                                    ToPurge.Add(Client.Value);
                                    continue;
                                }
                            }
                        }

                        foreach (SteamClient Client in ToPurge)
                        {
                            Log.Debug(String.Format("Purging client: {0}", Client.ClientID));
                            SteamServer.RemoveClient(Client.ClientID);
                        }
                        ToPurge.Clear();
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning(e.ToString());
            }
        }
    }
}
