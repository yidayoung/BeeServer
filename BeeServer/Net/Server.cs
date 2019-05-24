using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;




namespace Net
{

    public class ClientMsg
    {
        string address;
        public byte[] Msg { get; set; }

        public ClientMsg(string address, byte[] msg)
        {
            this.address = address;
            this.Msg = msg;
        }
    }

   
    public class Server
    {
        private List<ServerListener> _listeners = new List<ServerListener>();
        public event EventHandler<TcpClient> ClientConnected;
        public event EventHandler<TcpClient> ClientDisconnected;
        public event EventHandler<Message> DataReceived;
        private int messageid = 0;
        private int cur_tick = 0;
        private List<ClientMsg> msg_list;


        private void PrintMsg(object sender, Message message)
        {
            PtMessagePackage msg = PtMessagePackage.Read(message.Data);
            ByteBuffer buffer = new ByteBuffer(msg.Content);
            String address = message.TcpClient.Client.RemoteEndPoint.ToString();
            Console.WriteLine("client:" + address);
            long tick = buffer.ReadLong();
            Console.WriteLine("tick:" + tick);
            String act = buffer.ReadString();
            Console.WriteLine("act:" + act);
            if (act == "3")
            {
                foreach (ClientMsg cmsg in msg_list)
                {
                    Reply(message.TcpClient, cmsg.Msg);
                }
                ByteBuffer r_buffer = new ByteBuffer(msg.Content);
                r_buffer.WriteString("3");
                Reply(message.TcpClient, r_buffer.Getbuffer());
            }
            else
            { 
                msg_list.Add(new ClientMsg(address, msg.Content));
                BcAll(msg.Content);
            }
        }

       
        private void Reply(TcpClient client, byte[] msg)
        {
            NetworkStreamUtil.Write(client.GetStream(), PtMessagePackage.Write(PtMessagePackage.Build(messageid, msg)));
            messageid++;
        }

        private void BcAll(String msg)
        {
            Broadcast(PtMessagePackage.Build(messageid, System.Text.Encoding.Default.GetBytes(msg)));
            messageid++;
        }
        private void BcAll(byte[] msg)
        {
            Broadcast(PtMessagePackage.Build(messageid, msg));
            messageid++;
        }
        private void OnConnect(object sender, TcpClient tcpClient)
        {
            
            //foreach(ClientMsg msg in msg_list)
            //{
            //    NetworkStreamUtil.Write(tcpClient.GetStream(), msg.Msg);
            //}
            
        }
        public Server()
        {
            DataReceived += new EventHandler<Message>(PrintMsg);
            ClientConnected += new EventHandler<TcpClient>(OnConnect);
            msg_list = new List<ClientMsg>();
        }
        public void Start(int port)
        {
            var ipSorted = GetIPAddresses();
            foreach (var ipAddr in ipSorted)
            {
                try
                {
                    Start(ipAddr, port);
                }
                catch (SocketException ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }
        public void Start(IPAddress ipAddress, int port)
        {
            ServerListener listener = new ServerListener(this, ipAddress, port);
            _listeners.Add(listener);       
        }
        public void Stop()
        {
            _listeners.All(l => l.QueueStop = true);
            while (_listeners.Any(l => l.Listener.Active))
            {
                Thread.Sleep(100);
            };
            _listeners.Clear();
        }
        public void Broadcast(byte[] data)
        {          
            foreach (var client in _listeners.SelectMany(x => x.ConnectedClients))
            {
                NetworkStreamUtil.Write(client.GetStream(), data);                          
            }
        }
        public void Broadcast(PtMessagePackage package)
        {
            Broadcast(PtMessagePackage.Write(package));
        }

        public List<IPAddress> GetListeningIPs()
        {
            List<IPAddress> listenIps = new List<IPAddress>();
            foreach (var l in _listeners)
            {
                if (!listenIps.Contains(l.IPAddress))
                {
                    listenIps.Add(l.IPAddress);
                }
            }
            return listenIps.OrderByDescending(ip => RankIpAddress(ip)).ToList();
        }
        public IEnumerable<IPAddress> GetIPAddresses()
        {
            List<IPAddress> ipAddresses = new List<IPAddress>();

            IEnumerable<NetworkInterface> enabledNetInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up);
            foreach (NetworkInterface netInterface in enabledNetInterfaces)
            {
                IPInterfaceProperties ipProps = netInterface.GetIPProperties();
                foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                {
                    if (!ipAddresses.Contains(addr.Address))
                    {
                        ipAddresses.Add(addr.Address);
                    }
                }
            }

            var ipSorted = ipAddresses.OrderByDescending(ip => RankIpAddress(ip)).ToList();
            return ipSorted;
        }

        private int RankIpAddress(IPAddress addr)
        {
            int rankScore = 1000;

            if (IPAddress.IsLoopback(addr))
            {
                // rank loopback below others, even though their routing metrics may be better
                rankScore = 300;
            }
            else if (addr.AddressFamily == AddressFamily.InterNetwork)
            {
                rankScore += 100;
                // except...
                if (addr.GetAddressBytes().Take(2).SequenceEqual(new byte[] { 169, 254 }))
                {
                    // APIPA generated address - no router or DHCP server - to the bottom of the pile
                    rankScore = 0;
                }
            }

            if (rankScore > 500)
            {
                foreach (var nic in TryGetCurrentNetworkInterfaces())
                {
                    var ipProps = nic.GetIPProperties();
                    if (ipProps.GatewayAddresses.Any())
                    {
                        if (ipProps.UnicastAddresses.Any(u => u.Address.Equals(addr)))
                        {
                            // if the preferred NIC has multiple addresses, boost all equally
                            // (justifies not bothering to differentiate... IOW YAGNI)
                            rankScore += 1000;
                        }

                        // only considering the first NIC that is UP and has a gateway defined
                        break;
                    }
                }
            }

            return rankScore;
        }
        public void NotifyEndTransmissionRx(ServerListener listener, TcpClient client, byte[] msg)
        {
            if (DataReceived != null)
            {
                Message m = new Message(msg, client);
                DataReceived(this, m);
            }
        }

        public void NotifyClientConnected(ServerListener listener, TcpClient newClient)
        {
            Console.WriteLine("some one connect");
            ClientConnected?.Invoke(this, newClient);
        }

        public void NotifyClientDisconnected(ServerListener listener, TcpClient disconnectedClient)
        {
            Console.WriteLine("some one dis_connect");
            ClientDisconnected?.Invoke(this, disconnectedClient);
        }

        public int ConnectedClientsCount
        {
            get
            {
                return _listeners.Sum(l => l.ConnectedClientsCount);
            }
        }
        private static IEnumerable<NetworkInterface> TryGetCurrentNetworkInterfaces()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces().Where(ni => ni.OperationalStatus == OperationalStatus.Up);
            }
            catch (NetworkInformationException)
            {
                return Enumerable.Empty<NetworkInterface>();
            }
        }
    }
}
