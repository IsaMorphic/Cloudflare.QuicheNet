# Quiche.NET

[![NuGet](https://github.com/IsaMorphic/Cloudflare.QuicheNet/actions/workflows/nuget.yml/badge.svg)](https://github.com/IsaMorphic/Cloudflare.QuicheNet/actions/workflows/nuget.yml) [![NuGet version](https://img.shields.io/nuget/v/Cloudflare.QuicheNet?label=NuGet%20version)](https://www.nuget.org/packages/Cloudflare.QuicheNet)

A delicious C# wrapper for [Cloudflare's quiche](https://github.com/cloudflare/quiche) library. 

# How to use 

The wrapper exposes a few key classes that mirror the functionality of .NET's built-in QUIC implementation. Importantly, however, the wrapper is completely cross-platform and includes all native dependencies as part of the [NuGet package](https://www.nuget.org/packages/Cloudflare.QuicheNet). Simply install the package into your project and go!

## Server-side (`QuicheListener`)

```c#
// Initialize UDP socket (listening on port 8080)
Socket socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
socket.Bind(new IPEndPoint(IPAddress.Any, 8080));

// Configure listener
QuicheConfig config = new QuicheConfig 
{
    // Refer to official quiche documentation for more options;
    // certain options default to broken behavior if left unspecified.
    MaxInitialBidiStreams = 16,
    MaxInitialUniStreams = 16,
};

// Start listening for inbound QUIC connections
QuicheListener listener = new QuicheListener(socket, config);
_ = Task.Run(listener.ListenAsync);

// Wait for client to connect
QuicheConnection client = await listener.AcceptAsync();

// Accept inbound client stream
QuicheStream stream = await client.AcceptInboundStreamAsync();

// OR create outbound server stream
QuicheStream stream = await client.CreateOutboundStreamAsync(/* specify unidirectional or bidirectional */);
```

## Client-side (`QuicheConnection`)

```c#
// Initialize UDP socket (random port)
Socket socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
socket.Bind(new IPEndPoint(IPAddress.Any, 0));

// Configure client
QuicheConfig config = new QuicheConfig 
{
    // Refer to official quiche documentation for more options;
    // certain options default to broken behavior if left unspecified.
    MaxInitialBidiStreams = 16,
    MaxInitialUniStreams = 16,
};

// Initiate QUIC connection with server
QuicheConnection client = QuicheConnection.ConnectAsync(socket, /* server endpoint here */, config);

// Accept inbound server stream
QuicheStream stream = await client.AcceptInboundStreamAsync();

// OR create outbound client stream
QuicheStream stream = await client.CreateOutboundStreamAsync(/* specify unidirectional or bidirectional */);
```

## Cleaning up after Quiche

Since Quiche is a native library, it manages its own memory that must be cleaned up after explicitly. Quiche.NET facilitates this via the built-in .NET `IDisposable` interface which exists for this purpose. All objects that are initialized by Quiche.NET inherit from this interface and must be cleaned up to avoid memory leakage in your app. 

There is also a hierarchy of objects that must be disposed in a certain order to ensure that resources are not leaked. When creating a new `QuicheConnection`, disposing of it will always shutdown all constituent `QuicheStream`s immediately and call their `Dispose` method. Additionally, disposing of a `QuicheListener` object will shutdown all of its constituent `QuicheConnection`s, disconnecting all clients immediately. Finally, a given `QuicheConfig` should always be disposed of, but not before instantiating a `QuicheListener` or `QuicheConnection`.

## Working example (`ExampleApp`)

To compile and debug a working example application with minimal logic, see the `ExampleApp` project. This program starts a client and server within the same process on separate sockets to demonstrate a basic "Hello world" scenario. Full TLS authentication is also properly demonstrated. To run the example application, you must generate a self-signed SSL certificate. Use the following command to do so in the `trust` subdirectory:

```bash
openssl req -x509 -nodes -days 365 -newkey rsa:2048 -keyout key.pem -out cert.pem -addext "subjectAltName = DNS:localhost, IP:127.0.0.1"
```

Once the certificate is generated, the example application can be run for testing (e.g. `dotnet run`, IDE / debugger)

## Extras & Goodies

Quiche also supports sending / receiving `DATAGRAM` frames from any `Connection` instance. This feature of the QUIC protocol can be used to leverage the existing semantics of a UDP session and asynchronously send smaller messages to the other side which are unordered, but encrypted and reliable. This feature is supported by Quiche.NET and can be used with any `QuicheConnection` instance as follows:

```csharp
// Before connection init
QuicheConfig config = new QuicheConfig { /* ... */ };
config.SetDatagramOptions(new QuicheConfig.DatagramOptions 
{
    Enabled = true,
    SendQueueLength = 64,
    ReceiveQueueLength = 64,
});

// Once connected
QuicheConnection conn;

// Observe datagrams once enough data is available
if (conn.DatagramReceiveQueueSize > READ_THRESHOLD_BYTES)
{
    // Call will wait asynchronously for next DATAGRAM if none are available in the queue
    byte[] data = await conn.ReceiveDatagramAsync(); 
}

// Send datagrams until queue is saturated
if (conn.DatagramSendQueueSize < WRITE_THRESHOLD_BYTES)
{
    // Call will wait asynchronously until queue is no longer full
    byte[] data = /* ... */;
    await conn.SendDatagramAsync(data);
}
```

All queued DATAGRAMs are guaranteed to be delivered, and will be flushed reliably by Quiche before a connection closes. This feature is best suited to delivering smaller messages alongside long-running streams. These messages can be used, for example, to exchange SIP messages between two parties in a voice / video call, all within the same connection and without dealing with rate control limitations that streams have to deal with. 

# How it works

Quiche is an abstract implementation of the QUIC protocol. Although QUIC is intended to operate above UDP in the network protocol stack, quiche's implementation is uniquely cross-platform due to existing at a higher layer of abstraction- that is to say, quiche doesn't touch native sockets to do its work. Instead, quiche accepts packet data from the caller, allowing any socket abstraction to exist below it. This allows Quiche.NET to use .NET's `System.Net.Socket` API to transmit raw packet data and schedule packets for sending, while quiche itself unwraps & generates those packets for us whenever they enter or leave the system. 
