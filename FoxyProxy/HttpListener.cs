using System;
using System.Net;

namespace FoxyProxy
{
    public sealed class HttpListener : Listener
    {
        public HttpListener(int port) : this(IPAddress.Any, port) { }
        public HttpListener(IPAddress address, int port) : base(port, address) { }
        public override void OnAccept(IAsyncResult ar)
        {
            try
            {
                var newSocket = ListenSocket.EndAccept(ar);
                if (newSocket != null)
                {
                    var newClient = new HttpClient(newSocket, RemoveClient);
                    AddClient(newClient);
                    newClient.StartHandshake();
                }
            }
            catch { }
            try
            {
                //Restart Listening
                ListenSocket.BeginAccept(OnAccept, ListenSocket);
            }
            catch
            {
                Dispose();
            }
        }
        public override string ToString()
        {
            return "HTTP service on " + Address + ":" + Port;
        }
    }
}
