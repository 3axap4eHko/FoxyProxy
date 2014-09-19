using System;
using System.IO;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HttpListener = FoxyProxy.HttpListener;

namespace FoxyProxyTest
{
    [TestClass]
    public class UnitTest1
    {
        const string TestHost = "http://example.org/";
        const string ProxyHost = "localhost";
        const int ProxyPort = 9999;

        [TestMethod]
        public void PositiveProxyTest()
        {
            var proxy = new HttpListener(Dns.GetHostEntry(ProxyHost).AddressList[0], ProxyPort);
            proxy.Start();

            var request = WebRequest.Create(TestHost);
            request.Proxy = new WebProxy(ProxyHost, ProxyPort);
            var response = (HttpWebResponse)request.GetResponse();
            Assert.AreEqual(response.StatusCode, HttpStatusCode.OK);
            var dataStream = response.GetResponseStream();
            Assert.IsNotNull(dataStream);
            var reader = new StreamReader(dataStream);
            var responseFromServer = reader.ReadToEnd();
            Assert.IsFalse(String.IsNullOrEmpty(responseFromServer));
            reader.Close();
            dataStream.Close();
            response.Close();
            proxy.Dispose();
        }

        [TestMethod]
        [ExpectedException(typeof(WebException))]
        public void NegativeProxyTest()
        {
            var request = WebRequest.Create("http://example.org/");
            request.Proxy = new WebProxy(ProxyHost, ProxyPort);
            var response = (HttpWebResponse)request.GetResponse();
            response.Close();
        }
    }
}
