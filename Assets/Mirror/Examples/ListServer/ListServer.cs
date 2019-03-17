﻿// add this component to the NetworkManager
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Mirror.Examples.Listen
{
    public class ServerStatus
    {
        public string ip;
        //public ushort port; // <- not all transports use a port. assume default port. feel free to also send a port if needed.
        public string title;
        public ushort players;
        public ushort capacity;

        public int lastLatency = -1;
        public Ping ping;

        public ServerStatus(string ip, /*ushort port,*/ string title, ushort players, ushort capacity)
        {
            this.ip = ip;
            //this.port = port;
            this.title = title;
            this.players = players;
            this.capacity = capacity;
            ping = new Ping(ip);
        }
    }

    [RequireComponent(typeof(NetworkManager))]
    public class ListServer : MonoBehaviour
    {
        [Header("Listen Server Connection")]
        public string listenServerIp = "127.0.0.1";
        public ushort gameServerToListenPort = 8887;
        public ushort clientToListenPort = 8888;
        public string gameServerTitle = "Deathmatch";

        Telepathy.Client gameServerToListenConnection = new Telepathy.Client();
        Telepathy.Client clientToListenConnection = new Telepathy.Client();

        [Header("OnGUI")]
        public bool showOnGUI;
        public int windowWidth = 560;
        public int windowHeight = 400;
        public int titleWidth = 220;
        public int playersWidth = 60;
        public int addressWidth = 130;
        public int latencyWidth = 60;
        public int joinWidth = 50;
        Vector2 scrollPosition;

        [Header("UI")]
        public GameObject panel;
        public Transform content;
        public UIServerStatusSlot slotPrefab;

        // all the servers, stored as dict with unique ip key so we can
        // update them more easily
        // (use "ip:port" if port is needed)
        Dictionary<string, ServerStatus> list = new Dictionary<string, ServerStatus>();

        void Start()
        {
            // examples
            //list["127.0.0.1"] = new ServerStatus("127.0.0.1", "Deathmatch", 3, 10);
            //list["192.168.0.1"] = new ServerStatus("192.168.0.1", "Free for all", 7, 10);
            //list["172.217.22.3"] = new ServerStatus("172.217.22.3", "5vs5", 10, 10);
            //list["172.217.16.142"] = new ServerStatus("172.217.16.142", "Hide & Seek Mod", 0, 10);

            // Update once a second. no need to try to reconnect or read data
            // in each Update call
            // -> calling it more than 1/second would also cause significantly
            //    more broadcasts in the list server.
            InvokeRepeating(nameof(Tick), 0, 1);
        }

        // should we use the client to listen connection?
        bool UseClientToListen()
        {
            return !NetworkManager.IsHeadless() && !NetworkServer.active && !NetworkClient.active;
        }

        // should we use the game server to listen connection?
        bool UseGameServerToListen()
        {
            return NetworkServer.active;
        }

        void Tick()
        {
            TickGameServer();
            TickClient();
        }

        // send server status to list server
        void SendStatus()
        {
            BinaryWriter writer = new BinaryWriter(new MemoryStream());

            // create message
            // NOTE: you can send anything that you want, as long as you also
            //       receive it in ParseMessage
            char[] titleChars = gameServerTitle.ToCharArray();
            writer.Write((ushort)titleChars.Length);
            writer.Write(titleChars);
            writer.Write((ushort)NetworkServer.connections.Count);
            writer.Write((ushort)NetworkManager.singleton.maxConnections);

            // send it
            writer.Flush();
            gameServerToListenConnection.Send(((MemoryStream)writer.BaseStream).ToArray());
        }

        void TickGameServer()
        {
            // send server data to listen
            if (UseGameServerToListen())
            {
                // connected yet?
                if (gameServerToListenConnection.Connected)
                {
                    SendStatus();
                }
                // otherwise try to connect
                // (we may have just started the game)
                else if (!gameServerToListenConnection.Connecting)
                {
                    Debug.Log("Establishing game server to listen connection...");
                    gameServerToListenConnection.Connect(listenServerIp, gameServerToListenPort);
                }
            }
            // shouldn't use game server, but still using it?
            else if (gameServerToListenConnection.Connected)
            {
                gameServerToListenConnection.Disconnect();
            }
        }

        void ParseMessage(byte[] bytes)
        {
            // use binary reader because our NetworkReader uses custom string reading with bools
            // => we don't use ReadString here because the listen server doesn't
            //    know C#'s '7-bit-length + utf8' encoding for strings
            BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8);
            ushort ipLength = reader.ReadUInt16();
            string ip = new string(reader.ReadChars(ipLength));
            //ushort port = reader.ReadUInt16(); <- not all Transports use a port. assume default.
            ushort titleLength = reader.ReadUInt16();
            string title = new string(reader.ReadChars(titleLength));
            ushort players = reader.ReadUInt16();
            ushort capacity = reader.ReadUInt16();
            //Debug.Log("PARSED: ip=" + ip + /*" port=" + port +*/ " title=" + title + " players=" + players + " capacity= " + capacity);

            // build key
            string key = ip/* + ":" + port*/;

            // find existing or create new one
            ServerStatus server;
            if (list.TryGetValue(key, out server))
            {
                // refresh
                server.title = title;
                server.players = players;
                server.capacity = capacity;
            }
            else
            {
                // create
                server = new ServerStatus(ip, /*port,*/ title, players, capacity);
            }

            // save
            list[key] = server;
        }

        void TickClient()
        {
            // receive client data from listen
            if (UseClientToListen())
            {
                // connected yet?
                if (clientToListenConnection.Connected)
                {
                    // receive latest game server info
                    while (clientToListenConnection.GetNextMessage(out Telepathy.Message message))
                    {
                        // data message?
                        if (message.eventType == Telepathy.EventType.Data)
                            ParseMessage(message.data);
                    }

                    // ping again if previous ping finished
                    foreach (ServerStatus server in list.Values)
                    {
                        if (server.ping.isDone)
                        {
                            server.lastLatency = server.ping.time;
                            server.ping = new Ping(server.ip);
                        }
                    }
                }
                // otherwise try to connect
                // (we may have just joined the menu/disconnect from game server)
                else if (!clientToListenConnection.Connecting)
                {
                    Debug.Log("Establishing client to listen connection...");
                    clientToListenConnection.Connect(listenServerIp, clientToListenPort);
                }
            }
            // shouldn't use client, but still using it? (e.g. after joining)
            else if (clientToListenConnection.Connected)
            {
                clientToListenConnection.Disconnect();
                list.Clear();
            }

            // refresh UI afterwards
            OnUI();
        }

        void OnGUI()
        {
            if (!showOnGUI) return;

            // show listen data on client
            if (UseClientToListen())
            {
                GUILayout.BeginArea(new Rect(Screen.width/2f - windowWidth/2f, Screen.height/2f - windowHeight/2f, windowWidth, windowHeight));
                GUILayout.BeginVertical("box");

                // header
                GUILayout.Label("<b>Join Server</b>");

                if (!clientToListenConnection.Connected)
                    GUILayout.Label("Connecting...");

                if (clientToListenConnection.Connected && list.Count == 0)
                    GUILayout.Label("Scanning...");

                // scroll area
                scrollPosition = GUILayout.BeginScrollView(scrollPosition);

                // server table header
                GUILayout.BeginHorizontal("box");
                GUILayout.Box("<b>Server</b>", GUILayout.Width(titleWidth));
                GUILayout.Box("<b>Players</b>", GUILayout.Width(playersWidth));
                GUILayout.Box("<b>Latency</b>", GUILayout.Width(latencyWidth));
                GUILayout.Box("<b>Address</b>", GUILayout.Width(addressWidth));
                GUILayout.Box("<b>Action</b>", GUILayout.Width(joinWidth));
                GUILayout.EndHorizontal();

                // entries
                foreach (ServerStatus server in list.Values)
                {
                    GUILayout.BeginHorizontal("box");
                    GUILayout.Box(server.title, GUILayout.Width(titleWidth));
                    GUILayout.Box(server.players + "/" + server.capacity, GUILayout.Width(playersWidth));
                    GUILayout.Box(server.lastLatency != -1 ? server.lastLatency.ToString() : "...", GUILayout.Width(latencyWidth));
                    GUILayout.Box(server.ip, GUILayout.Width(addressWidth));
                    GUI.enabled = server.players < server.capacity && !NetworkClient.active;
                    if (GUILayout.Button("Join", GUILayout.Width(joinWidth)))
                    {
                        NetworkManager.singleton.networkAddress = server.ip;
                        NetworkManager.singleton.StartClient();
                    }
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();

                GUILayout.EndVertical();

                // bottom panel to start host/server
                GUILayout.BeginVertical("box");
                GUILayout.Label("<b>Start Server</b>");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Server & Play"))
                    NetworkManager.singleton.StartHost();
                if (GUILayout.Button("Server Only"))
                    NetworkManager.singleton.StartServer();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                GUILayout.EndArea();
            }
        }

        // instantiate/remove enough prefabs to match amount
        public static void BalancePrefabs(GameObject prefab, int amount, Transform parent)
        {
            // instantiate until amount
            for (int i = parent.childCount; i < amount; ++i)
            {
                GameObject go = GameObject.Instantiate(prefab);
                go.transform.SetParent(parent, false);
            }

            // delete everything that's too much
            // (backwards loop because Destroy changes childCount)
            for (int i = parent.childCount-1; i >= amount; --i)
                GameObject.Destroy(parent.GetChild(i).gameObject);
        }

        void OnUI()
        {
            // instantiate/destroy enough slots
            BalancePrefabs(slotPrefab.gameObject, list.Count, content);

            // refresh all members
            for (int i = 0; i < list.Values.Count; ++i)
            {
                UIServerStatusSlot slot = content.GetChild(i).GetComponent<UIServerStatusSlot>();
                ServerStatus server = list.Values.ToList()[i];
                slot.titleText.text = server.title;
                slot.playersText.text = server.players + "/" + server.capacity;
                slot.latencyText.text = server.lastLatency != -1 ? server.lastLatency.ToString() : "...";
                slot.addressText.text = server.ip;
                slot.joinButton.interactable = server.players < server.capacity && !NetworkClient.active;
                slot.joinButton.onClick.RemoveAllListeners();
                slot.joinButton.onClick.AddListener(() => {
                    NetworkManager.singleton.networkAddress = server.ip;
                    NetworkManager.singleton.StartClient();
                });
            }
        }

        // disconnect everything when pressing Stop in the Editor
        void OnApplicationQuit()
        {
            if (gameServerToListenConnection.Connected)
                gameServerToListenConnection.Disconnect();
            if (clientToListenConnection.Connected)
                clientToListenConnection.Disconnect();
        }
    }
}