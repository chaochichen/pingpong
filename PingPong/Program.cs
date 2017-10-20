using System;
using System.Threading;

namespace PingPong
{

    public class Program
    {
        public static void Main(string[] args)
        {
            bool forceActive = false;
            int? myPort = null;
            int? peerPort = null;
            string peerHost = null;
            string id = null;
            foreach(string arg in args)
            {
                if (arg.StartsWith("--force-active="))
                {
                    string[] s = arg.Split('=');
                    forceActive = (s[1].Equals("true") ? true : false);
                }
                else if (arg.StartsWith("--my-port="))
                {
                    string[] s = arg.Split('=');
                    myPort = Convert.ToInt32(s[1]);
                }
                else if (arg.StartsWith("--peer-port="))
                {
                    string[] s = arg.Split('=');
                    peerPort = Convert.ToInt32(s[1]);
                }
                else if (arg.StartsWith("--peer-host="))
                {
                    string[] s = arg.Split('=');
                    peerHost = s[1];
                }
                else if (arg.StartsWith("--id="))
                {
                    string[] s = arg.Split('=');
                    id = s[1];
                }
            }
            Console.WriteLine("ForceActive={0}, myPort={1}, peerHost = {2}, PeerPort={3}", forceActive, myPort, peerHost, peerPort);
            if (myPort != null && peerPort != null && peerHost != null && id != null)
            {
                var fsm = new WRGStateMachine(forceActive, myPort.Value, peerHost, peerPort.Value);
                fsm.WrgID = id;
                while (true) { Thread.Sleep(1000); }
            }
            else
            {
                Console.WriteLine("Usage: --force-active=true|false --my-port=[myPort] --peer-port=[peerPort] --id=[wrgId]");
            }
        }
    }
}
