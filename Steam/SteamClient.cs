/*
	All files containing this header is released under the GPL 3.0 license.

	Initial author: (https://github.com/)Convery
	Started: 2014-10-28
	Notes:
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace SteamServer
{
    class SteamClient
    {
        // Identifiers.
        public Byte[] Username; // Ingame name.
        public UInt32 ClientID; // Index for arrays.
        public UInt64 XUID;     // Ingame identifier.
        public UInt64 HWID;     // Hardware identifier.
        public UInt64 AppID;    // Game identifier.

        // Networking.
        public Socket ClientSocket;
        public static Int32 BufferSize = 2048;          // A client should generally send less than 1024 bytes of data per packet.
        public Byte[] Buffer = new Byte[BufferSize];    // The data sent will be stored here.
        public DateTime LastPacket = DateTime.Now;      // We need to drop the client if it times out.
        public IPAddress GetIP()
        {
            try
            {
                return (ClientSocket.RemoteEndPoint as IPEndPoint).Address;
            }
            catch (SocketException)
            {
                return null;
            }
        }

        // Client-client communication.
        public Queue<SteamMessage> MessageQueue = new Queue<SteamMessage>();
        public bool EnqueueMessage(SteamMessage Message)
        {
            try
            {
                // Impose a 5 sec delay on messages from the same XUID.
                if (Math.Floor((DateTime.Now - MessageQueue.Last(item => item.IM.FromXUID == Message.IM.FromXUID).Timestamp).TotalSeconds) < 5)
                    return false;

                MessageQueue.Enqueue(Message);
                return true;
            }
            catch (Exception e)
            {
                Log.Warning(e.Message);
                return false;
            }
        }
        public bool EnqueueMessage(UInt64 ToXUID, Byte[] Message)
        {
            SteamMessage NewMessage = new SteamMessage();

            NewMessage.Timestamp = DateTime.Now;
            NewMessage.IM.TimeStamp = (UInt32)DateTime.Now.ToFileTime();
            NewMessage.IM.FromXUID = ToXUID;
            NewMessage.IM.ToXUID = XUID;
            NewMessage.IM.Message = Message;

            return EnqueueMessage(NewMessage);
        }

        // Friendslist.
        public PersonaState SocialStatus = PersonaState.Online;
        public List<SteamFriend> FriendsList = new List<SteamFriend>();
        void UpdateFriendslistFromAPI()
        {
            FriendsList.Clear();
            // Something something GET HHS.com/API/Friendslist.php?XUID={0}
        }
        void UpdateFriendslistFromClients()
        {
            lock (SteamServer.Clients)
            {
                FriendsList.Clear();

                foreach (UInt32 ClientKey in SteamServer.Clients.Keys)
                {
                    if (ClientID != ClientKey)
                        FriendsList.Add(SteamFriend.CreateFromClient(ClientKey));
                }

                // Debug
                SteamFriend NewFriend = new SteamFriend();
                NewFriend.XUID = 0x1100001DEADC0DE;
                NewFriend.Username = Encoding.ASCII.GetBytes("DummyFriend");
                NewFriend.Status = PersonaState.Online;
                FriendsList.Add(NewFriend);
            }
        }
        public void UpdateFriendslist()
        {
            if (SteamServer.Anonymous)
                UpdateFriendslistFromClients();
            else
                UpdateFriendslistFromAPI();
        }

        // Authentication.
        public Boolean isAuthenticated = false;
        public UInt32 SessionID = 0;


    }
}
