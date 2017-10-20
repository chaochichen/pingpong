using Stateless;
using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using Newtonsoft.Json;

namespace PingPong
{
    internal class WRGStatus
    {
        public string WRGId { set; get; }
        public bool ACTIVE { set; get; }
        public WRGStatus(string id, bool status)
        {
            WRGId = id;
            ACTIVE = status;
        }
    }

    internal class MyTimer
    {
        public int duration { set; get; }
        private Timer timer = null;

        public MyTimer(TimerCallback cb, int duration)
        {
            var autoEvent = new AutoResetEvent(false);
            this.duration = duration;
            timer = new Timer(cb, autoEvent, duration, Timeout.Infinite);
        }
        public void stop()
        {
            timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        public void restart()
        {
            timer.Change(duration, Timeout.Infinite);
        }
    }

    internal class WRGStateMachine
    {
        //
        // constants
        //
        static int MAX_Connect_Attempts { set; get; } = 3;
        static int MAX_Missing_Pong { set; get; } = 3;
        static int Wait_for_Pong_Duration = 1000;
        static int Ping_Duration = 1000;

        //
        // State machine
        //
        enum State { Init, StartUp, ConnectPeer, WaitForPong, WRGActive }
        enum Trigger { StartUp, ForceActive, FailedToPeer, ConnectToPeer, ConnectedToPeer, PeerLostConnection, FailedToReceivePong, ElectedActive, PingPeer }
        private StateMachine<State, Trigger> stateMachine;

        //
        // properties
        //
        bool Active { set; get; }
        public string WrgID { set; get; }
        int MyPort { set; get; }
        int PeerPort { set; get; }
        string PeerHost { set; get; }
        bool ConnectedToPeer { set; get; } = false;

        //
        // member variables
        //
        private MyTimer waitForPongTimer = null;
        private MyTimer PingTimer = null;
        private TcpClient Peer = null;
        private int pongNotReceived = 0;
        private TcpListener pingListener;
        public WRGStateMachine(bool forceActive, int myPort, string peerHost, int peerPort)
        {
            MyPort = myPort;
            PeerPort = peerPort;
            PeerHost = peerHost;

            //
            // state machine definitions
            //
            stateMachine = new StateMachine<State, Trigger>(State.Init);
            stateMachine.Configure(State.WRGActive)
                .OnEntry(() => OnWRGActive())
                .Permit(Trigger.StartUp, State.StartUp);

            stateMachine.Configure(State.WaitForPong)
                .OnEntry(() => OnWaitForPong())
                .Permit(Trigger.PeerLostConnection, State.ConnectPeer)
                .Permit(Trigger.FailedToReceivePong, State.WRGActive)
                .Permit(Trigger.ElectedActive, State.WRGActive);

            stateMachine.Configure(State.StartUp)
                .OnEntryAsync(async () => await OnStartUp(forceActive))
                .Permit(Trigger.ForceActive, State.WRGActive)
                .Permit(Trigger.ConnectToPeer, State.ConnectPeer);

            stateMachine.Configure(State.ConnectPeer)
                .OnEntry(() => OnConnectPeer())
                .Permit(Trigger.ConnectedToPeer, State.WaitForPong)
                .Permit(Trigger.FailedToPeer, State.WRGActive);

            stateMachine.Configure(State.Init)
                .Permit(Trigger.StartUp, State.StartUp);

            stateMachine.FireAsync(Trigger.StartUp);
        }

        private void OnWaitForPong()
        {
            //Console.WriteLine("OnWaitForPong");
            try
            {
                if (waitForPongTimer == null)
                {
                    waitForPongTimer = new MyTimer(OnMissingPong, Wait_for_Pong_Duration);
                }
                else
                {
                    waitForPongTimer.restart();
                }
                pingPeer();
                NetworkStream stream = Peer.GetStream();
                byte[] pongBuffer = new byte[100];
                int size = stream.Read(pongBuffer, 0, 100);
                if (size > 0)
                {
                    OnPongReceived(pongBuffer);
                }
            }
            catch (Exception e)
            {
                waitForPongTimer.stop();
                Console.WriteLine("\nLost socket connection to remote server");
                if (stateMachine.State == State.WaitForPong)
                {
                    stateMachine.Fire(Trigger.PeerLostConnection);
                }
            }
        }
        private void pingPeer()
        {
            NetworkStream stream = Peer.GetStream();
            byte[] pingBuffer = new byte[1];
            stream.Write(pingBuffer, 0, 1);
            stream.Flush();
            Console.Write("i");
        }
        private void OnMissingPong(object stateInfo)
        {
            Console.WriteLine("OnMissingPong");
            AutoResetEvent autoEvent = (AutoResetEvent)stateInfo;
            autoEvent.Set();
            if (++pongNotReceived >= MAX_Missing_Pong)
            {
                Console.WriteLine("Didn't receive a pong after {0} pings", MAX_Missing_Pong);
                stateMachine.Fire(Trigger.FailedToReceivePong);
            }
            else
            {
                OnWaitForPong();
            }
        }

        private void OnPingReceived(Socket client)
        {
            // reply with a pong
            WRGStatus myWrgStatus = new WRGStatus(WrgID, Active);
            string json = JsonConvert.SerializeObject(myWrgStatus);
            client.Send(Encoding.ASCII.GetBytes(json));
            Console.Write("o");
        }
        private void OnPongReceived(byte[] pongBuffer)
        {
            pongNotReceived = 0;
            waitForPongTimer.stop();
            string pong = Encoding.UTF8.GetString(pongBuffer);
            WRGStatus wrgStatus = JsonConvert.DeserializeObject<WRGStatus>(pong);
            processPong(wrgStatus);
        }
        private void OnPingExpired(object stateInfo)
        {
            //Console.WriteLine("OnPingExpired");
            AutoResetEvent autoEvent = (AutoResetEvent)stateInfo;
            autoEvent.Set();
            OnWaitForPong();
        }

        private void processPong(WRGStatus peerStatus)
        {
            try
            {
                if (this.Active && peerStatus.ACTIVE)
                {
                    throw new Exception("<<<More than one ACTIVE WRGs detected!!! Warm the system administrator immediately.>>>");
                }
                if (this.WrgID.Equals(peerStatus.WRGId))
                {
                    throw new Exception("<<<Detected another WRG with the same WRGId: '" + this.WrgID + "' Please alert the system administrator immediately.>>>");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

            if (!peerStatus.ACTIVE && peerStatus.WRGId.CompareTo(WrgID) > 0)
            {
                stateMachine.Fire(Trigger.ElectedActive);
            }
            else
            {
                if (PingTimer == null)
                {
                    PingTimer = new MyTimer(OnPingExpired, Ping_Duration);
                }
                else
                {
                    PingTimer.restart();
                }
            }
        }
        private void OnWRGActive()
        {
            //Console.WriteLine("OnWRGActive");
            Active = true;
            if (Peer != null)
            {
                try
                {
                    if (Peer.Connected)
                    {
                        Peer.Client.Shutdown(SocketShutdown.Both);
                    }
                    Peer.Dispose();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            }
            Console.WriteLine("\n[[[WRG Active]]]");
        }
        private void OnConnectPeer()
        {
            Console.WriteLine("Connecting to Peer @ {0}:{1}", PeerHost, PeerPort);
            ConnectedToPeer = false;
            for (var i = 0; i < MAX_Connect_Attempts && !ConnectedToPeer; i++)
            {
                Console.Write("C");
                Peer = new TcpClient();
                IAsyncResult ar = Peer.ConnectAsync(PeerHost, PeerPort);
                System.Threading.WaitHandle wh = ar.AsyncWaitHandle;
                if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(1200)))
                {
                    Console.Write("x");
                }
                else
                {
                    ConnectedToPeer = true;
                }
            }
            if (ConnectedToPeer)
            {
                Console.WriteLine("\nConnected to {0}:{1}, start sending ping...", PeerHost, PeerPort);
                stateMachine.FireAsync(Trigger.ConnectedToPeer);
            }
            else
            {
                Console.WriteLine(" - couldn't reach to peer");
                stateMachine.FireAsync(Trigger.FailedToPeer);
            }
        }

        async private Task OnStartUp(bool forceActive)
        {
            var server = Dns.GetHostName();
            var heserver = await Dns.GetHostEntryAsync(server);
            var ipAddress = heserver.AddressList[2];
            var ep = new IPEndPoint(ipAddress, MyPort);
            try
            {
                pingListener = new TcpListener(ep);
                pingListener.Start();

                var th = new Thread(ProcessPing);
                th.Start();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            if (forceActive)
            {
                stateMachine.Fire(Trigger.ForceActive);
            }
            else
            {
                Active = false;
                stateMachine.Fire(Trigger.ConnectToPeer);
            }
        }

        private async void ProcessPing()
        {
            Console.Write("Waiting for a connection... ");
            while (true)
            {
                Socket client = await pingListener.AcceptSocketAsync();
                Console.WriteLine("Client connected from {0}", client.RemoteEndPoint.ToString());

                // get a thread to handle the ping from the new client connection
                var childSocketThread = new Thread(() =>
                {
                    Console.WriteLine("In ping requests processing thread...");
                    byte[] data = new byte[100];
                    while (true)
                    {
                        try
                        {
                            int size = client.Receive(data);
                            OnPingReceived(client);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Remote peer disconnected.");
                            break;
                        }
                    }
                    Console.WriteLine("Exiting ping requests processing thread...");
                });
                childSocketThread.Start();
            }
        }
    }
}