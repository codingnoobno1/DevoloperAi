using System;
using System.Net.Sockets;

namespace Syncro.Desktop.Services.Universe.Runtime
{
    /// <summary>Cheap TCP readiness probe — "is something answering on this port yet".</summary>
    internal static class PortProbe
    {
        public static bool IsOpen(int port, int timeoutMs = 400)
        {
            if (port <= 0) return false;
            try
            {
                using var client = new TcpClient();
                var connect = client.BeginConnect("127.0.0.1", port, null, null);
                if (connect.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeoutMs)))
                {
                    client.EndConnect(connect);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
