using System;
using System.Net;

namespace Rtsp.Utils.Tests;

#if NETFRAMEWORK
public class TcpListener : System.Net.Sockets.TcpListener, IDisposable
{
    public TcpListener(IPEndPoint localEP): base(localEP) {}
    public TcpListener(IPAddress localaddr, int port): base(localaddr, port) {}
    
    
    public void Dispose()
    {
        this.Stop();
    }
}
#endif