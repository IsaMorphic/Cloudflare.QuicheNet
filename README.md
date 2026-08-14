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
    // Refer to official quiche documentation for more options
    MaxInitialBidiStreams = 16,
    MaxInitialUniStreams = 16,
};

// Start listening for inbound QUIC connections
QuicheListener listener = new QuicheListener(socket, config);
_ = Task.Run(() => listener.ListenAsync(CancellationToken.None));

// Wait for client to connect
QuicheConnection client = await listener.AcceptAsync(CancellationToken.None);

// Accept inbound client stream
QuicheStream stream = client.AcceptInboundStreamAsync(CancellationToken.None);

// OR create outbound server stream
QuicheStream stream = client.CreateOutboundStreamAsync(CancellationToken.None, /* specify unidirectional or bidirectional */);
```

## Client-side (`QuicheConnection`)

```c#
// Initialize UDP socket (random port)
Socket socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
socket.Bind(new IPEndPoint(IPAddress.Any, 0));

// Configure client
QuicheConfig config = new QuicheConfig 
{
    // Refer to official quiche documentation for more options
    MaxInitialBidiStreams = 16,
    MaxInitialUniStreams = 16,
};

// Initiate QUIC connection with server
QuicheConnection client = QuicheConnection.Connect(socket, /* server endpoint here */, config);

// Accept inbound server stream
QuicheStream stream = client.AcceptInboundStreamAsync(CancellationToken.None);

// OR create outbound client stream
QuicheStream stream = client.CreateOutboundStreamAsync(CancellationToken.None, /* specify unidirectional or bidirectional */);
```

# How it works

Quiche is an abstract implementation of the QUIC protocol. Although QUIC is intended to operate above UDP in the network protocol stack, quiche's implementation is uniquely cross-platform due to existing at a higher layer of abstraction- that is to say, quiche doesn't touch native sockets to do its work. Instead, quiche accepts packet data from the caller, allowing any socket abstraction to exist below it. This allows Quiche.NET to use .NET's `System.Net.Socket` API to transmit raw packet data and schedule packets for sending, while quiche itself unwraps & generates those packets for us whenever they enter or leave the system. 
