using NUnit.Framework;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Rtsp.Tests
{
    [TestFixture]
    public class UdpSocketTests
    {
        private const int StartPort = 57200;
        private const int EndPort = 57400;

        [Test]
        public void SocketsAreIPv4ByDefault()
        {
            using var socket = new UDPSocket(StartPort, EndPort);

            Assert.That(socket.Ports.First, Is.GreaterThanOrEqualTo(StartPort));
        }

        [Test]
        public void SocketsCanBeOpenedForIPv6()
        {
            if (!Socket.OSSupportsIPv6)
            {
                Assert.Ignore("No IPv6 on this machine");
            }

            using var socket = new UDPSocket(StartPort + 20, EndPort, AddressFamily.InterNetworkV6);

            // it can be given an IPv6 destination, which an IPv4 socket cannot send to
            Assert.DoesNotThrow(() => socket.SetDataDestination("::1", socket.Ports.First + 100));
        }

        [Test]
        public void AnIPv4SocketIsNotGivenAnIPv6Destination()
        {
            using var socket = new UDPSocket(StartPort + 40, EndPort);

            // Taking whatever the resolver returned first meant a socket could be pointed at an
            // address of the other family, and every send then failed.
            Assert.Throws<System.ArgumentException>(() => socket.SetDataDestination("::1", 5000));
        }

        [Test]
        public void MediaReachesAnIPv6Destination()
        {
            if (!Socket.OSSupportsIPv6)
            {
                Assert.Ignore("No IPv6 on this machine");
            }

            using var receiver = new UdpClient(0, AddressFamily.InterNetworkV6);
            int receiverPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;

            using var sender = new UDPSocket(StartPort + 60, EndPort, AddressFamily.InterNetworkV6);
            sender.SetDataDestination("::1", receiverPort);

            byte[] sent = { 0x80, 0x60, 0x00, 0x01, 0xAA, 0xBB };
            sender.WriteToDataPort(sent);

            receiver.Client.ReceiveTimeout = 5000;
            var from = new IPEndPoint(IPAddress.IPv6Any, 0);
            byte[] received = receiver.Receive(ref from);

            Assert.That(received, Is.EqualTo(sent));
        }

        [Test]
        public void MediaStillReachesAnIPv4Destination()
        {
            using var receiver = new UdpClient(0, AddressFamily.InterNetwork);
            int receiverPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;

            using var sender = new UDPSocket(StartPort + 80, EndPort);
            sender.SetDataDestination("127.0.0.1", receiverPort);

            byte[] sent = { 0x80, 0x60, 0x00, 0x02, 0xCC, 0xDD };
            sender.WriteToDataPort(sent);

            receiver.Client.ReceiveTimeout = 5000;
            var from = new IPEndPoint(IPAddress.Any, 0);
            byte[] received = receiver.Receive(ref from);

            Assert.That(received, Is.EqualTo(sent));
        }
    }
}
