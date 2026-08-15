using Cloudflare.QuicheNet;
using System.Net;
using System.Net.Sockets;
using System.Text;

async Task RunListenerAsync(CancellationToken cancellationToken)
{
    // Socket init
    using Socket socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
    socket.Bind(new IPEndPoint(IPAddress.Loopback, 8080));

    // Listener configuration
    using QuicheConfig config = new QuicheConfig
    {
        MaxInitialBidiStreams = 16,
        MaxInitialUniStreams = 16,
        MaxInitialDataSize = 4096,
        MaxInitialUniStreamDataSize = 4096,
        MaxInitialLocalBidiStreamDataSize = 4096,
        MaxInitialRemoteBidiStreamDataSize = 4096,
    };
    config.SetApplicationProtocols("test");
    config.LoadPrivateKeyFromPemFile("trust/key.pem");
    config.LoadCertificateChainFromPemFile("trust/cert.pem");

    // Listener init
    using QuicheListener listener = new QuicheListener(socket, config);
    
    // Listener logic
    using CancellationTokenSource cts = new();
    Task listenTask = listener.ListenAsync(cts.Token);

    // Server logic
    using (QuicheConnection client = await listener.AcceptAsync(cancellationToken))
    {
        await client.ConnectionEstablished;
        await RunServerAsync(client, cancellationToken); 
        cts.Cancel();
    }

    // Wait for exit
    await listenTask;
}

async Task RunServerAsync(QuicheConnection client, CancellationToken cancellationToken)
{
    // Open outbound download stream
    using (QuicheStream stream = await client.CreateOutboundStreamAsync(QuicheStream.Direction.Unidirectional, cancellationToken))
    using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
    {
        await writer.WriteLineAsync("Hello world!");
    }

    // Wait for connection to close gracefully
    while (!cancellationToken.IsCancellationRequested && !client.IsClosed)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(75);
    }
}

async Task RunClientAsync(CancellationToken cancellationToken)
{
    // Socket init
    using Socket socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

    // Client configuration
    using QuicheConfig config = new QuicheConfig
    {
        MaxInitialBidiStreams = 16,
        MaxInitialUniStreams = 16,
        MaxInitialDataSize = 4096,
        MaxInitialUniStreamDataSize = 4096,
        MaxInitialLocalBidiStreamDataSize = 4096,
        MaxInitialRemoteBidiStreamDataSize = 4096,
    };
    config.SetApplicationProtocols("test");

    // Open client connection
    using QuicheConnection client = QuicheConnection.Connect(socket, new IPEndPoint(IPAddress.Loopback, 8080), config, "localhost");
    await client.ConnectionEstablished;

    // Accept inbound download stream
    using (QuicheStream stream = await client.AcceptInboundStreamAsync(cancellationToken))
    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
    {
        // Download stream content
        string output = await reader.ReadToEndAsync(cancellationToken);
        Console.Write(output);
    }
}

using CancellationTokenSource cts = new();

Console.CancelKeyPress += CancelKeyPress;

void CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
{
    e.Cancel = true;
    cts.Cancel();
}

try
{
    await Task.WhenAll(
        Task.Run(() => RunListenerAsync(cts.Token)),
        Task.Run(() => RunClientAsync(cts.Token))
        );
}
catch (OperationCanceledException) { }