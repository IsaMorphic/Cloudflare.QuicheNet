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
    Console.WriteLine("Server: wait for client connection");
    using (QuicheConnection client = await listener.AcceptAsync(cancellationToken))
    {
        Console.WriteLine("Server: connected to client");
        await Task.Run(() => RunServerAsync(client, cancellationToken));
        cts.Cancel(); // Stop listening
    }

    // Wait for exit
    await listenTask;
}

async Task RunServerAsync(QuicheConnection client, CancellationToken cancellationToken)
{
    // Open outbound download stream
    Console.WriteLine("Server: initiating download stream");
    using (QuicheStream stream = await client.CreateOutboundStreamAsync(QuicheStream.Direction.Unidirectional, cancellationToken)) 
    using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
    {
        Console.WriteLine("Server: writing stream content");
        await writer.WriteAsync("Hello, Client!");
    }

    Console.WriteLine("Server: waiting for response stream");
    using (QuicheStream stream = await client.AcceptInboundStreamAsync(cancellationToken))
    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
    {
        Console.WriteLine("Server: reading response content");
        string content = await reader.ReadToEndAsync(cancellationToken);
        Console.WriteLine($"Server: client says \"{content}\"");
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
    Console.WriteLine("Client: connecting to server");
    using QuicheConnection client = await QuicheConnection.ConnectAsync(
        socket, new IPEndPoint(IPAddress.Loopback, 8080), config,
        "localhost", cancellationToken: cancellationToken);

    Console.WriteLine("Client: waiting for download stream");
    using (QuicheStream stream = await client.AcceptInboundStreamAsync(cancellationToken))
    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
    {
        // Download stream content
        Console.WriteLine("Client: reading requested content");
        string content = await reader.ReadToEndAsync(cancellationToken);
        Console.WriteLine($"Client: server says \"{content}\"");
    }

    using (QuicheStream stream = await client.CreateOutboundStreamAsync(QuicheStream.Direction.Unidirectional, cancellationToken))
    using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
    {
        Console.WriteLine("Client: writing response content");
        await writer.WriteAsync("Hello, Server!");
    }

    // Wait for connection to close gracefully
    while (!cancellationToken.IsCancellationRequested && !client.IsClosed)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(75);
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