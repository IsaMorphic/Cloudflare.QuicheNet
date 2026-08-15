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

## Working example (`ExampleApp`)

To compile and debug a working example application with minimal logic, see the `ExampleApp` project. This program starts a client and server within the same process on separate sockets to demonstrate a basic "Hello world" scenario. Full TLS authentication is also properly demonstrated. To run the example application, you must generate a self-signed SSL certificate. Use the following command to do so in the `trust` subdirectory:

```bash
openssl req -x509 -nodes -days 365 -newkey rsa:2048 -keyout key.pem -out cert.pem -addext "subjectAltName = DNS:localhost, IP:127.0.0.1"
```

Once the certificate is generated, the example application can be run for testing.

# How it works

Quiche is an abstract implementation of the QUIC protocol. Although QUIC is intended to operate above UDP in the network protocol stack, quiche's implementation is uniquely cross-platform due to existing at a higher layer of abstraction- that is to say, quiche doesn't touch native sockets to do its work. Instead, quiche accepts packet data from the caller, allowing any socket abstraction to exist below it. This allows Quiche.NET to use .NET's `System.Net.Socket` API to transmit raw packet data and schedule packets for sending, while quiche itself unwraps & generates those packets for us whenever they enter or leave the system. 
