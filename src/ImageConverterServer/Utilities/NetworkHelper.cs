using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace ImageConverterServer.Utilities
{
    internal static class NetworkHelper
    {
        internal static bool IsLocalPortBusy(int port)
        {
            bool isBusy = false;

            IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] activeTcpListeners = ipGlobalProperties.GetActiveTcpListeners();
            TcpConnectionInformation[] tcpConnections = ipGlobalProperties.GetActiveTcpConnections();

            foreach (IPEndPoint listener in activeTcpListeners)
            {
                if (listener.Port == port)
                {
                    isBusy = true;
                }
            }

            foreach (TcpConnectionInformation connection in tcpConnections)
            {
                if (connection.LocalEndPoint.Port == port)
                {
                    isBusy = true;
                }
            }
            return isBusy;
        }
    }
}
