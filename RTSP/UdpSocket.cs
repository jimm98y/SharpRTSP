using Rtsp.Messages;
using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Rtsp
{
    public class UDPSocket : IRtpTransport
    {
        protected readonly UdpClient dataSocket;
        protected readonly UdpClient controlSocket;

        private readonly CancellationTokenSource _cancellationTokenSource = new();

        private Task? _dataReadTask;
        private Task? _controlReadTask;
        private IPEndPoint? _dataEndPoint;
        private IPEndPoint? _controlEndPoint;

        private bool disposedValue;

        public int DataPort { get; protected set; }
        public int ControlPort { get; protected set; }

        public PortCouple Ports => new(DataPort, ControlPort);

        /// <summary>
        /// Initializes a new instance of the <see cref="UDPSocket"/> class.
        /// Creates two new UDP sockets using the start and end Port range
        /// </summary>
        public UDPSocket(int startPort, int endPort) : this(startPort, endPort, AddressFamily.InterNetwork)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UDPSocket"/> class.
        /// Creates two new UDP sockets using the start and end Port range
        /// </summary>
        /// <param name="addressFamily">
        /// The address family to open the sockets in. Use <see cref="AddressFamily.InterNetworkV6"/>
        /// to send RTP to a client that is reached over IPv6.
        /// </param>
        public UDPSocket(int startPort, int endPort, AddressFamily addressFamily)
        {
            // open a pair of UDP sockets - one for data (video or audio) and one for the status channel (RTCP messages)
            DataPort = startPort;
            ControlPort = startPort + 1;

            bool ok = false;
            while (!ok && (ControlPort < endPort))
            {
                // Video/Audio port must be odd and command even (next one)
                try
                {
                    dataSocket = new UdpClient(DataPort, addressFamily);
                    controlSocket = new UdpClient(ControlPort, addressFamily);
                    ok = true;
                }
                catch (SocketException)
                {
                    // Fail to allocate port, try again
                    dataSocket?.Close();
                    controlSocket?.Close();

                    // try next data or control port
                    DataPort += 2;
                    ControlPort += 2;
                }

                if (ok)
                {
                    dataSocket!.Client.ReceiveBufferSize = 100 * 1024;
                    dataSocket!.Client.SendBufferSize = 65535; // default is 8192. Make it as large as possible for large RTP packets which are not fragmented

                    if (addressFamily == AddressFamily.InterNetwork)
                    {
                        // an IPv4 socket option, and not supported on every platform for IPv6
                        controlSocket!.Client.DontFragment = false;
                    }
                }
            }

            if (dataSocket == null || controlSocket == null)
            {
                throw new InvalidOperationException("UDP socket was not initialized (can't find free UDP port), can't continue");
            }
        }

        protected UDPSocket(UdpClient dataSocket, UdpClient controlSocket)
        {
            this.dataSocket = dataSocket;
            this.controlSocket = controlSocket;
        }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        public void Start()
        {
            if (_dataReadTask != null)
            {
                throw new InvalidOperationException("Forwarder was stopped, can't restart it");
            }

            _dataReadTask = Task.Factory.StartNew(async () =>
                await DoWorkerJobAsync(dataSocket, OnDataReceived, DataPort, _cancellationTokenSource.Token).ConfigureAwait(false),
                TaskCreationOptions.LongRunning);
            _controlReadTask = Task.Factory.StartNew(async () =>
                await DoWorkerJobAsync(controlSocket, OnControlReceived, ControlPort, _cancellationTokenSource.Token).ConfigureAwait(false),
                TaskCreationOptions.LongRunning);
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public virtual void Stop()
        {
            _cancellationTokenSource.Cancel();
            dataSocket.Close();
            controlSocket.Close();
        }

        /// <summary>
        /// Occurs when data is received.
        /// </summary>
        public event EventHandler<RtspDataEventArgs>? DataReceived;

        /// <summary>
        /// Raises the <see cref="E:DataReceived"/> event.
        /// </summary>
        protected void OnDataReceived(RtspDataEventArgs rtspDataEventArgs)
        {
            DataReceived?.Invoke(this, rtspDataEventArgs);
        }

        /// <summary>
        /// Occurs when control is received.
        /// </summary>
        public event EventHandler<RtspDataEventArgs>? ControlReceived;

        /// <summary>
        /// Raises the <see cref="E:ControlReceived"/> event.
        /// </summary>
        protected void OnControlReceived(RtspDataEventArgs rtspDataEventArgs)
        {
            ControlReceived?.Invoke(this, rtspDataEventArgs);
        }

        /// <summary>
        /// Does the video job.
        /// </summary>
        private static async Task DoWorkerJobAsync(UdpClient client, Action<RtspDataEventArgs> handler, int port, CancellationToken cancellation)
        {
            try
            {
                // to be compatible with netstandard2.0 we can't use the memory directly for the receive call 
                byte[] buffer = new byte[65536];
                // loop until we get an exception eg the socket closed or the cancellation token is set
                while (!cancellation.IsCancellationRequested)
                {
#if NET7_0_OR_GREATER
                    var size = await client.Client.ReceiveAsync(buffer, cancellation).ConfigureAwait(false);
#else
                    // Task to prevent warning and keep the same code than .NET 8
                    var size = await Task.FromResult(client.Client.Receive(buffer)).ConfigureAwait(false);
#endif
                    var bufferOwner = MemoryPool<byte>.Shared.Rent(size);
                    buffer.AsSpan()[..size].CopyTo(bufferOwner.Memory.Span);

                    handler(new RtspDataEventArgs(new RtspData(bufferOwner, size)
                    {
                        Channel = port,
                    }));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        public void SetDataDestination(string hostname, int port)
        {
            _dataEndPoint = ResolveDestination(dataSocket, hostname, port);
        }

        public void SetControlDestination(string hostname, int port)
        {
            _controlEndPoint = ResolveDestination(controlSocket, hostname, port);
        }

        /// <summary>
        /// Picks an address for the hostname that the given socket can actually send to.
        /// </summary>
        /// <remarks>
        /// A name can resolve to both IPv4 and IPv6 addresses, and a socket can only send to its own
        /// family, so taking the first address returned fails whenever that is the other one. A dual
        /// mode socket can reach an IPv4 address through its mapped form.
        /// </remarks>
        private static IPEndPoint ResolveDestination(UdpClient socket, string hostname, int port)
        {
            var addresses = Dns.GetHostAddresses(hostname);
            if (addresses.Length == 0)
            {
                throw new ArgumentException("No IP address found for the hostname", nameof(hostname));
            }

            var family = socket.Client.AddressFamily;

            foreach (var address in addresses)
            {
                if (address.AddressFamily == family)
                {
                    return new IPEndPoint(address, port);
                }
            }

            if (family == AddressFamily.InterNetworkV6 && socket.Client.DualMode)
            {
                foreach (var address in addresses)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return new IPEndPoint(address.MapToIPv6(), port);
                    }
                }
            }

            throw new ArgumentException(
                $"No {family} address found for the hostname", nameof(hostname));
        }

        public void WriteToControlPort(ReadOnlySpan<byte> data) => controlSocket.Send(data, _controlEndPoint);

        public Task WriteToControlPortAsync(ReadOnlyMemory<byte> data)
            => controlSocket.SendAsync(data, _controlEndPoint, _cancellationTokenSource.Token).AsTask();

        public void WriteToDataPort(ReadOnlySpan<byte> data) => dataSocket.Send(data, _dataEndPoint);

        public Task WriteToDataPortAsync(ReadOnlyMemory<byte> data)
            => dataSocket.SendAsync(data, _dataEndPoint, _cancellationTokenSource.Token).AsTask();

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Stop();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Ne changez pas ce code. Placez le code de nettoyage dans la méthode 'Dispose(bool disposing)'
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
