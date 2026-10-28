namespace Rtsp.Tests;

using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

#if NETFRAMEWORK
using Rtsp.Utils.Tests;
using TcpListener = Rtsp.Utils.Tests.TcpListener;
#endif

using NUnit.Framework;



[TestFixture]
public class RtspTcpTlsTransportTests
{
    private readonly TimeSpan _timeoutServer = TimeSpan.FromSeconds(10);
    
    [Test]
    public void AnRtspsConnectionChecksTheCertificateAgainstTheHostName()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(() =>
        {
            using var certificate = CreateCertificate();
            using var client = listener.AcceptTcpClient();
            using var tls = new SslStream(client.GetStream(), false);
            tls.AuthenticateAsServer(certificate, false, SslProtocols.Tls12, false);
        });

        var errors = SslPolicyErrors.None;
        using var transport = new RtspTcpTlsTransport(new Uri($"rtsps://localhost:{port}/stream"),
            (_, _, _, policyErrors) => { errors |= policyErrors; return true; });
        transport.GetStream();
        
        Assert.That(serving.Wait(_timeoutServer), Is.True, "the server did not finish");
        // the certificate names localhost: checked against the address, it does not match
        Assert.That(errors & SslPolicyErrors.RemoteCertificateNameMismatch, Is.EqualTo(SslPolicyErrors.None));
    }
    
    [Test]
    public void AnRtspsConnectionMadeElsewhereChecksTheCertificateAgainstTheHostNameGiven()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(() =>
        {
            using var certificate = CreateCertificate();
            using var client = listener.AcceptTcpClient();
            using var tls = new SslStream(client.GetStream(), false);
            tls.AuthenticateAsServer(certificate, false, SslProtocols.Tls12, false);
        });

        var errors = SslPolicyErrors.None;
        using var transport = new RtspTcpTlsTransport(new TcpClient("localhost", port),
            (_, _, _, policyErrors) => { errors |= policyErrors; return true; })
        {
            TargetHost = "localhost",
        };
        transport.GetStream();
        
        Assert.That(serving.Wait(_timeoutServer), Is.True, "the server did not finish");
        Assert.That(errors & SslPolicyErrors.RemoteCertificateNameMismatch, Is.EqualTo(SslPolicyErrors.None));
    }
    
    /// <summary>A self-signed certificate for localhost, with its key, as a TLS server needs it.</summary>
    private static X509Certificate2 CreateCertificate()
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());

        using X509Certificate2 created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // through PKCS#12, as Windows only serves TLS with a key it can find again
        byte[] pfx = created.Export(X509ContentType.Pfx);
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(pfx, null);
#else
        return new X509Certificate2(pfx);
#endif
    }

}