using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FoxyProxy
{
    public delegate void DestroyDelegate(IClient client);
    public class HttpClient : IClient
    {
        public HttpClient(Socket clientSocket, DestroyDelegate destroyer)
        {
            ClientSocket = clientSocket;
            _destroyer = destroyer;
            RequestedPath = null;
            HeaderFields = null;
        }

        private StringDictionary HeaderFields { get; set; }

        private string HttpVersion
        {
            get
            {
                return _httpVersion;
            }
            set
            {
                _httpVersion = value;
            }
        }
        private string HttpRequestType
        {
            get
            {
                return _httpRequestType;
            }
            set
            {
                _httpRequestType = value;
            }
        }

        public string RequestedPath { get; set; }

        private string HttpQuery
        {
            get
            {
                return _httpQuery;
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException();
                _httpQuery = value;
            }
        }

        internal Socket ClientSocket
        {
            get
            {
                return _clientSocket;
            }
            set
            {
                if (_clientSocket != null)
                {
                    _clientSocket.Close();
                }
                _clientSocket = value;
            }
        }
        internal Socket DestinationSocket
        {
            get
            {
                return _destinationSocket;
            }
            set
            {
                if (_destinationSocket != null)
                {
                    _destinationSocket.Close();
                }
                _destinationSocket = value;
            }
        }
        protected byte[] Buffer
        {
            get
            {
                return _buffer;
            }
        }
        protected byte[] RemoteBuffer
        {
            get
            {
                return _remoteBuffer;
            }
        }
        public void Dispose()
        {
            try
            {
                ClientSocket.Shutdown(SocketShutdown.Both);
            }
            catch
            { }
            try
            {
                DestinationSocket.Shutdown(SocketShutdown.Both);
            }
            catch { }
            //Close the sockets
            if (ClientSocket != null)
                ClientSocket.Close();
            if (DestinationSocket != null)
                DestinationSocket.Close();
            //Clean up
            ClientSocket = null;
            DestinationSocket = null;
            if (_destroyer != null)
                _destroyer(this);
        }
        public override string ToString()
        {
            try
            {
                return "Incoming connection from " + ((IPEndPoint)DestinationSocket.RemoteEndPoint).Address;
            }
            catch
            {
                return "Client connection";
            }
        }
        public void StartRelay()
        {
            try
            {
                ClientSocket.BeginReceive(Buffer, 0, Buffer.Length, SocketFlags.None, OnClientReceive, ClientSocket);
                DestinationSocket.BeginReceive(RemoteBuffer, 0, RemoteBuffer.Length, SocketFlags.None, OnRemoteReceive, DestinationSocket);
            }
            catch
            {
                Dispose();
            }
        }
        protected void OnClientReceive(IAsyncResult ar)
        {
            try
            {
                var ret = ClientSocket.EndReceive(ar);
                if (ret <= 0)
                {
                    Dispose();
                    return;
                }
                DestinationSocket.BeginSend(Buffer, 0, ret, SocketFlags.None, OnRemoteSent, DestinationSocket);
            }
            catch
            {
                Dispose();
            }
        }
        protected void OnRemoteSent(IAsyncResult ar)
        {
            try
            {
                var ret = DestinationSocket.EndSend(ar);
                if (ret > 0)
                {
                    ClientSocket.BeginReceive(Buffer, 0, Buffer.Length, SocketFlags.None, OnClientReceive, ClientSocket);
                    return;
                }
            }
            catch { }
            Dispose();
        }
        protected void OnRemoteReceive(IAsyncResult ar)
        {
            try
            {
                var ret = DestinationSocket.EndReceive(ar);
                if (ret <= 0)
                {
                    Dispose();
                    return;
                }
                ClientSocket.BeginSend(RemoteBuffer, 0, ret, SocketFlags.None, OnClientSent, ClientSocket);
            }
            catch
            {
                Dispose();
            }
        }
        protected void OnClientSent(IAsyncResult ar)
        {
            try
            {
                var ret = ClientSocket.EndSend(ar);
                if (ret > 0)
                {
                    DestinationSocket.BeginReceive(RemoteBuffer, 0, RemoteBuffer.Length, SocketFlags.None, OnRemoteReceive, DestinationSocket);
                    return;
                }
            }
            catch { }
            Dispose();
        }

        public void StartHandshake()
        {
            try
            {
                ClientSocket.BeginReceive(Buffer, 0, Buffer.Length, SocketFlags.None, OnReceiveQuery, ClientSocket);
            }
            catch
            {
                Dispose();
            }
        }
        private bool IsValidQuery(string query)
        {
            var index = query.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (index == -1)
                return false;
            HeaderFields = ParseQuery(query);
            if (!HttpRequestType.ToUpper().Equals("POST")) return true;
            try
            {
                var length = int.Parse(HeaderFields["Content-Length"]);
                return query.Length >= index + 6 + length;
            }
            catch
            {
                SendBadRequest();
                return true;
            }
        }
        private void ProcessQuery(string query)
        {
            HeaderFields = ParseQuery(query);
            if (HeaderFields == null || !HeaderFields.ContainsKey("Host"))
            {
                SendBadRequest();
                return;
            }
            int port;
            string host;
            int ret;
            if (HttpRequestType.ToUpper().Equals("CONNECT"))
            { //HTTPS
                ret = RequestedPath.IndexOf(":", StringComparison.Ordinal);
                if (ret >= 0)
                {
                    host = RequestedPath.Substring(0, ret);
                    port = RequestedPath.Length > ret + 1 ? int.Parse(RequestedPath.Substring(ret + 1)) : 443;
                }
                else
                {
                    host = RequestedPath;
                    port = 443;
                }
            }
            else
            { //HTTP
                ret = HeaderFields["Host"].IndexOf(":", StringComparison.Ordinal);
                if (ret > 0)
                {
                    host = HeaderFields["Host"].Substring(0, ret);
                    port = int.Parse(HeaderFields["Host"].Substring(ret + 1));
                }
                else
                {
                    host = HeaderFields["Host"];
                    port = 80;
                }
                if (HttpRequestType.ToUpper().Equals("POST"))
                {
                    var index = query.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    _httpPost = query.Substring(index + 4);
                }
            }
            try
            {
                var destinationEndPoint = new IPEndPoint(Dns.GetHostEntry(host).AddressList[0], port);
                DestinationSocket = new Socket(destinationEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                if (HeaderFields.ContainsKey("Proxy-Connection") && HeaderFields["Proxy-Connection"].ToLower().Equals("keep-alive"))
                    DestinationSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, 1);
                DestinationSocket.BeginConnect(destinationEndPoint, OnConnected, DestinationSocket);
            }
            catch
            {
                SendBadRequest();
            }
        }
        private StringDictionary ParseQuery(string query)
        {
            var retdict = new StringDictionary();
            var lines = query.Replace("\r\n", "\n").Split('\n');
            int cnt, ret;
            //Extract requested URL
            if (lines.Length > 0)
            {
                //Parse the Http Request Type
                ret = lines[0].IndexOf(' ');
                if (ret > 0)
                {
                    HttpRequestType = lines[0].Substring(0, ret);
                    lines[0] = lines[0].Substring(ret).Trim();
                }
                //Parse the Http Version and the Requested Path
                ret = lines[0].LastIndexOf(' ');
                if (ret > 0)
                {
                    HttpVersion = lines[0].Substring(ret).Trim();
                    RequestedPath = lines[0].Substring(0, ret);
                }
                else
                {
                    RequestedPath = lines[0];
                }
                //Remove http:// if present
                if (RequestedPath.Length >= 7 && RequestedPath.Substring(0, 7).ToLower().Equals("http://"))
                {
                    ret = RequestedPath.IndexOf('/', 7);
                    RequestedPath = ret == -1 ? "/" : RequestedPath.Substring(ret);
                }
            }
            for (cnt = 1; cnt < lines.Length; cnt++)
            {
                ret = lines[cnt].IndexOf(":", StringComparison.Ordinal);
                if (ret <= 0 || ret >= lines[cnt].Length - 1) continue;
                try
                {
                    retdict.Add(lines[cnt].Substring(0, ret), lines[cnt].Substring(ret + 1).Trim());
                }
                catch
                { }
            }
            return retdict;
        }
        private void SendBadRequest()
        {
            const string badRequestContent = "HTTP/1.1 400 Bad Request\r\nConnection: close\r\nContent-Type: text/html\r\n\r\n<html><head><title>400 Bad Request</title></head><body><div align=\"center\"><table border=\"0\" cellspacing=\"3\" cellpadding=\"3\" bgcolor=\"#C0C0C0\"><tr><td><table border=\"0\" width=\"500\" cellspacing=\"3\" cellpadding=\"3\"><tr><td bgcolor=\"#B2B2B2\"><p align=\"center\"><strong><font size=\"2\" face=\"Verdana\">400 Bad Request</font></strong></p></td></tr><tr><td bgcolor=\"#D1D1D1\"><font size=\"2\" face=\"Verdana\"> The proxy server could not understand the HTTP request!<br><br> Please contact your network administrator about this problem.</font></td></tr></table></center></td></tr></table></div></body></html>";
            try
            {
                ClientSocket.BeginSend(Encoding.ASCII.GetBytes(badRequestContent), 0, badRequestContent.Length, SocketFlags.None, OnErrorSent, ClientSocket);
            }
            catch
            {
                Dispose();
            }
        }

        private string RebuildQuery()
        {
            var ret = HttpRequestType + " " + RequestedPath + " " + HttpVersion + "\r\n";
            if (HeaderFields == null) return ret;
            ret = HeaderFields.Keys.Cast<string>().Where(sc => sc.Length < 6 || !sc.Substring(0, 6).Equals("proxy-")).Aggregate(ret, (current, sc) => current + (System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(sc) + ": " + HeaderFields[sc] + "\r\n"));
            ret += "\r\n";
            if (_httpPost != null)
                ret += _httpPost;
            return ret;
        }

        public string ToString(bool withUrl)
        {
            string ret;
            try
            {
                if (DestinationSocket == null || DestinationSocket.RemoteEndPoint == null)
                    ret = "Incoming HTTP connection from " + ((IPEndPoint)ClientSocket.RemoteEndPoint).Address;
                else
                    ret = "HTTP connection from " + ((IPEndPoint)ClientSocket.RemoteEndPoint).Address + " to " + ((IPEndPoint)DestinationSocket.RemoteEndPoint).Address + " on port " + ((IPEndPoint)DestinationSocket.RemoteEndPoint).Port;
                if (HeaderFields != null && HeaderFields.ContainsKey("Host") && RequestedPath != null)
                    ret += "\r\n" + " requested URL: http://" + HeaderFields["Host"] + RequestedPath;
            }
            catch
            {
                ret = "HTTP Connection";
            }
            return ret;
        }
        private void OnReceiveQuery(IAsyncResult ar)
        {
            int ret;
            try
            {
                ret = ClientSocket.EndReceive(ar);
            }
            catch
            {
                ret = -1;
            }
            if (ret <= 0)
            { //Connection is dead :(
                Dispose();
                return;
            }
            HttpQuery += Encoding.ASCII.GetString(Buffer, 0, ret);
            //if received data is valid HTTP request...
            if (IsValidQuery(HttpQuery))
            {
                ProcessQuery(HttpQuery);
                //else, keep listening
            }
            else
            {
                try
                {
                    ClientSocket.BeginReceive(Buffer, 0, Buffer.Length, SocketFlags.None, OnReceiveQuery, ClientSocket);
                }
                catch
                {
                    Dispose();
                }
            }
        }
        private void OnErrorSent(IAsyncResult ar)
        {
            try
            {
                ClientSocket.EndSend(ar);
            }
            catch { }
            Dispose();
        }
        private void OnConnected(IAsyncResult ar)
        {
            try
            {
                DestinationSocket.EndConnect(ar);
                string rq;
                if (HttpRequestType.ToUpper().Equals("CONNECT"))
                { //HTTPS
                    rq = HttpVersion + " 200 Connection established\r\nProxy-Agent: Mentalis Proxy Server\r\n\r\n";
                    ClientSocket.BeginSend(Encoding.ASCII.GetBytes(rq), 0, rq.Length, SocketFlags.None, OnOkSent, ClientSocket);
                }
                else
                { //Normal HTTP
                    rq = RebuildQuery();
                    DestinationSocket.BeginSend(Encoding.ASCII.GetBytes(rq), 0, rq.Length, SocketFlags.None, OnQuerySent, DestinationSocket);
                }
            }
            catch
            {
                Dispose();
            }
        }
        private void OnQuerySent(IAsyncResult ar)
        {
            try
            {
                if (DestinationSocket.EndSend(ar) == -1)
                {
                    Dispose();
                    return;
                }
                StartRelay();
            }
            catch
            {
                Dispose();
            }
        }
        private void OnOkSent(IAsyncResult ar)
        {
            try
            {
                if (ClientSocket.EndSend(ar) == -1)
                {
                    Dispose();
                    return;
                }
                StartRelay();
            }
            catch
            {
                Dispose();
            }
        }
        private string _httpQuery = "";
        private string _httpVersion = "";
        private string _httpRequestType = "";
        private string _httpPost;
        private readonly DestroyDelegate _destroyer;
        private Socket _clientSocket;
        private Socket _destinationSocket;
        private readonly byte[] _buffer = new byte[4096]; //0<->4095 = 4096
        private readonly byte[] _remoteBuffer = new byte[1024]; //0<->1023 = 1024
    }
}
