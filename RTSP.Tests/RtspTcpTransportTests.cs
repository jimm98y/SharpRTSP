namespace Rtsp.Tests;

using System.Net;
using System.Net.Sockets;
using NUnit.Framework;

#if NETFRAMEWORK
using TcpListener = Rtsp.Utils.Tests.TcpListener;
#endif

[TestFixture]
public class RtspTcpTransportTests
{
 
    [Test]
    public void ATcpTransportReconnectsToAnIPv6Server()
    {
        using var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var connection = new TcpClient(AddressFamily.InterNetworkV6);
        connection.Connect(IPAddress.IPv6Loopback, port);
        using var first = listener.AcceptTcpClient();

        using var transport = new RtspTcpTransport(connection);
        transport.Close();
        transport.Reconnect();
        using var second = listener.AcceptTcpClient();

        Assert.That(transport.Connected, Is.True);
    }
    
    [Test]
    public void ATcpTransportReconnectsToAnIPv4ServerReachedOverADualModeSocket()
    {
        // the remote end point of a dual mode socket is the IPv4 address mapped into IPv6
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var connection = new TcpClient(AddressFamily.InterNetworkV6);
        connection.Client.DualMode = true;
        connection.Connect(IPAddress.Loopback, port);
        using var first = listener.AcceptTcpClient();

        using var transport = new RtspTcpTransport(connection);
        transport.Close();
        transport.Reconnect();
        using var second = listener.AcceptTcpClient();

        Assert.That(transport.Connected, Is.True);
    }
}