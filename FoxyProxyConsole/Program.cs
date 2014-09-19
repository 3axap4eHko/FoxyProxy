using System;
using System.Net;
using System.Net.Sockets;
using HttpListener = FoxyProxy.HttpListener;

namespace FoxyProxyConsole
{
    class Program
    {
        private static HttpListener _proxy;
        private static IPAddress _address;
        private static uint _port;
        static void Main(string[] args)
        {
            Console.WriteLine("Foxy Proxy v.0.0.1 Greetings you!");
            SetupProxy();
            StartProxy();
            Console.WriteLine("Type help or ? for command list");
            while (true)
            {
                string command;
                do
                {
                    Console.Write("cmd > ");
                } while (String.IsNullOrEmpty(command = Console.ReadLine()));
                switch (command)
                {
                    case "q":
                    case "quit":
                        return;
                    case "r":
                    case "restart":
                        _proxy.Dispose();
                        SetupProxy();
                        StartProxy();
                        return;
                    case "help":
                    case "?":
                        ShowHelp();
                        break;
                    default:
                        Console.WriteLine("Unkonown command");
                        ShowHelp();
                        break;
                }
            }
        }

        public static void ShowHelp()
        {
            Console.WriteLine("\thelp (?) - show this help");
            Console.WriteLine("\tdump (d) - dump to file");
            Console.WriteLine("\tcache (c) [1/0] - enable/disable cache");
            Console.WriteLine("\trestart (r) - restart proxy");
            Console.WriteLine("\tquit (q) - close proxy");
        }

        public static void StartProxy()
        {
            try
            {
                _proxy = new HttpListener(_address, (int)_port);
                _proxy.Start();
                Console.WriteLine("Foxy Proxy is Started!");
            }
            catch
            {
                throw new SocketException();
            }

        }

        public static void SetupProxy()
        {
            do
            {
                try
                {
                    Console.Write("Please enter proxy host [localhost]: ");
                    var host = Console.ReadLine();
                    host = String.IsNullOrEmpty(host) ? "127.0.0.1" : host;
                    var addresses = Dns.GetHostEntry(host).AddressList;
                    var selected = 0U;
                    if (addresses.Length > 1)
                    {
                        do
                        {
                            Console.Write("Chose proxy IP address [0]:");
                            for (var idx = 1; idx < addresses.Length; idx++)
                            {
                                Console.WriteLine("[{0,2}] {1}", idx, addresses[idx]);
                            }
                        } while (!uint.TryParse(Console.ReadLine(), out selected) && selected < addresses.Length);
                    }
                    _address = addresses[selected];
                    Console.WriteLine("Proxy IP is: " + _address);
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Error]" + ex.Message);
                }
            } while (true);
            Console.Write("Please enter proxy port [8080]: ");
            if (!uint.TryParse(Console.ReadLine(), out _port))
            {
                _port = 8080;
            }
            Console.WriteLine("Proxy port is: " + _port);
        }

        public static void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
}
