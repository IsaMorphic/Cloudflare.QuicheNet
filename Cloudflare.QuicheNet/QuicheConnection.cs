using Cloudflare.Quiche;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

using static Cloudflare.Quiche.NativeMethods;
using static Cloudflare.QuicheNet.QuicheStream;

namespace Cloudflare.QuicheNet;

public class QuicheConnection : IDisposable
{
    private static (MemoryHandle, int) GetSocketAddress(EndPoint? endPoint)
    {
        Memory<byte>? buf = endPoint?.Serialize().Buffer;
        return buf is null ? default : (buf.Value.Pin(), buf.Value.Length);
    }

    internal static QuicheConnection Accept(Socket socket,
        EndPoint remoteEndPoint, ReadOnlyMemory<byte> initialData,
        QuicheConfig config, byte[]? cid = null)
    {
        EndPoint localEndPoint = socket.LocalEndPoint ?? throw new ArgumentException(
            "Given socket was not bound to a valid local endpoint!", nameof(socket));

        var (local, local_len) = GetSocketAddress(localEndPoint);
        var (remote, remote_len) = GetSocketAddress(remoteEndPoint);

        using (local)
        using (remote)
        {
            byte[] scidBuf = (byte[]?)cid?.Clone() ?? RandomNumberGenerator
                .GetBytes((int)QuicheLibrary.MAX_CONN_ID_LEN);
            unsafe
            {
                fixed (byte* scidPtr = scidBuf)
                {
                    return new(quiche_accept(
                        scidPtr, (nuint)scidBuf.Length, null, 0,
                        (sockaddr*)local.Pointer, (size_t)local_len,
                        (sockaddr*)remote.Pointer, (size_t)remote_len,
                        config.NativePtr),
                        config, socket, remoteEndPoint,
                        initialData, scidBuf);
                }
            }
        }
    }

    public static async Task<QuicheConnection> ConnectAsync(Socket socket, EndPoint remoteEndPoint,
        QuicheConfig config, string? hostname = null, byte[]? cid = null, CancellationToken cancellationToken = default)
    {
        EndPoint localEndPoint = socket.LocalEndPoint ?? throw new ArgumentException(
            "Given socket was not bound to a valid local endpoint!", nameof(socket));

        var (local, local_len) = GetSocketAddress(localEndPoint);
        var (remote, remote_len) = GetSocketAddress(remoteEndPoint);

        using (local)
        using (remote)
        {
            QuicheConnection conn;
            byte[] hostnameBuf = Encoding.UTF8.GetBytes([.. hostname?.ToCharArray() ?? [], '\u0000']);
            byte[] scidBuf = (byte[]?)cid?.Clone() ?? RandomNumberGenerator
                .GetBytes((int)QuicheLibrary.MAX_CONN_ID_LEN);
            unsafe
            {
                fixed (byte* hostnamePtr = hostnameBuf)
                fixed (byte* scidPtr = scidBuf)
                {
                    conn = new(quiche_connect(hostnamePtr,
                        scidPtr, (nuint)scidBuf.Length,
                        (sockaddr*)local.Pointer, (size_t)local_len,
                        (sockaddr*)remote.Pointer, (size_t)remote_len,
                        config.NativePtr),
                        config, socket, remoteEndPoint,
                        ReadOnlyMemory<byte>.Empty, scidBuf);
                }
            }

            await conn.ConnectionEstablished.WaitAsync(cancellationToken);
            return conn;
        }
    }

    private const int MAX_STREAM_SEND_RETRIES = 10;

    private readonly QuicheConfig config;

    private readonly Task? listenTask, recvDgramTask, sendDgramTask;
    private readonly Task recvTask, recvStreamTask, sendTask, sendStreamTask;
    private readonly CancellationTokenSource cts;

    private readonly TaskCompletionSource establishedTcs;
    private readonly ConcurrentDictionary<ulong, QuicheStream> streamMap;
    private readonly Channel<QuicheStream> streamChannel;

    private readonly Channel<byte[]>? dgramSendChannel;
    private readonly Channel<byte[]>? dgramRecvChannel;

    private readonly Socket socket;
    private readonly EndPoint remoteEndPoint;

    internal readonly ConcurrentDictionary<ulong, byte[]> sendQueue;
    internal readonly ConcurrentQueue<ReadOnlyMemory<byte>> recvQueue;

    private readonly byte[] connectionId;
    internal ReadOnlySpan<byte> ConnectionId => connectionId;

    internal unsafe quiche_conn* NativePtr { get; private set; }

    internal Task ConnectionEstablished => establishedTcs.Task;

    public long DatagramReceiveQueueSize
    {
        get
        {
            unsafe
            {
                lock (this)
                {
                    return NativePtr is null ? 0 : (long)NativePtr->DgramRecvQueueByteSize();
                }
            }
        }
    }

    public long DatagramSendQueueSize
    {
        get
        {
            unsafe
            {
                lock (this)
                {
                    return NativePtr is null ? 0 : (long)NativePtr->DgramSendQueueByteSize();
                }
            }
        }
    }

    public bool IsClosed
    {
        get
        {
            unsafe
            {
                lock (this)
                {
                    return NativePtr is null || NativePtr->IsClosed();
                }
            }
        }
    }

    public bool IsServer
    {
        get
        {
            unsafe
            {
                lock (this)
                {
                    return NativePtr is not null && NativePtr->IsServer();
                }
            }
        }
    }

    public long MaxDatagramSize
    {
        get
        {
            unsafe
            {
                lock (this)
                {
                    return NativePtr is null ? 0 : (long)NativePtr->DgramMaxWritableLen();
                }
            }
        }
    }

    private unsafe QuicheConnection(quiche_conn* nativePtr, QuicheConfig config, Socket socket, EndPoint remoteEndPoint, ReadOnlyMemory<byte> initialData, ReadOnlyMemory<byte> connectionId)
    {
        NativePtr = nativePtr;

        this.config = config;

        this.socket = socket;
        this.remoteEndPoint = remoteEndPoint;

        this.connectionId = new byte[QuicheLibrary.MAX_CONN_ID_LEN];
        connectionId.CopyTo(this.connectionId);

        sendQueue = new();
        recvQueue = new();

        streamMap = new();
        streamChannel = Channel.CreateUnbounded<QuicheStream>();

        if (config.DatagramOptions.Enabled)
        {
            dgramSendChannel = Channel.CreateBounded<byte[]>(config.DatagramOptions.SendQueueLength);
            dgramRecvChannel = Channel.CreateBounded<byte[]>(config.DatagramOptions.ReceiveQueueLength);
        }

        establishedTcs = new();

        cts = new();

        recvTask = Task.Run(() => ReceiveAsync(cts.Token));
        sendTask = Task.Run(() => SendAsync(cts.Token));

        if (config.DatagramOptions.Enabled)
        {
            recvDgramTask = Task.Run(() => ReceiveDatagramsAsync(cts.Token));
            sendDgramTask = Task.Run(() => SendDatagramsAsync(cts.Token));
        }

        recvStreamTask = Task.Run(() => ReceiveStreamAsync(cts.Token));
        sendStreamTask = Task.Run(() => SendStreamAsync(cts.Token));

        if (initialData.IsEmpty)
        {
            listenTask = Task.Run(() => ListenAsync(cts.Token));
        }
        else
        {
            recvQueue.Enqueue(initialData);
        }
    }

    private class SendScheduleInfo
    {
        public int SendCount { get; set; }
        public byte[]? SendBuffer { get; set; }
    }

    private void SendPacket(object? state)
    {
        SendScheduleInfo? info = state as SendScheduleInfo;
        if (info is not null)
        {
            lock (info)
            {
                if (info.SendBuffer is not null)
                {
                    int bytesSent = 0;
                    while (bytesSent < info.SendCount)
                    {
                        var packetSpan = info.SendBuffer.AsSpan(bytesSent, info.SendCount - bytesSent);
                        bytesSent += socket.SendTo(packetSpan, remoteEndPoint);
                    }
                }
            }
        }
    }

    private async Task SendAsync(CancellationToken cancellationToken)
    {
        byte[] packetBuf = new byte[QuicheLibrary.MAX_DATAGRAM_LEN];

        SendScheduleInfo info = new() { SendBuffer = packetBuf };
        using Timer timer = new Timer(SendPacket, info, Timeout.Infinite, Timeout.Infinite);

        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosed)
            {
                throw new QuicheException(QuicheError.QUICHE_ERR_DONE, "Connection was closed.");
            }

            try
            {
                long resultOrError;
                quiche_send_info sendInfo = default;
                unsafe
                {
                    lock (this)
                    {
                        fixed (byte* pktPtr = info.SendBuffer)
                        {
                            resultOrError = (long)NativePtr->Send(
                                pktPtr, (nuint)info.SendBuffer.Length,
                                (quiche_send_info*)Unsafe.AsPointer(ref sendInfo)
                                );
                        }
                    }
                }

                QuicheException.ThrowIfError((QuicheError)resultOrError);

                lock (info)
                {
                    info.SendCount = (int)resultOrError;
                }

                timer.Change(
                    TimeSpan.FromSeconds(Unsafe.As<timespec, CLong>
                        (ref sendInfo.at).Value) +
                    TimeSpan.FromTicks(sendInfo.at.tv_nsec.Value / 100),
                    Timeout.InfiniteTimeSpan
                    );
            }
            catch (QuicheException ex)
            when (ex.ErrorCode == QuicheError.QUICHE_ERR_DONE)
            {
                if (IsClosed) { throw; }
                await Task.Delay(75, cancellationToken);
                continue;
            }
            catch (QuicheException ex)
            {
                establishedTcs.TrySetException(ex);
                throw;
            }
            catch (OperationCanceledException)
            {
                establishedTcs.TrySetCanceled(cancellationToken);
                throw;
            }
        }
    }

    private async Task SendDatagramsAsync(CancellationToken cancellationToken)
    {
        byte[] dgramBuf;
        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosed)
            {
                throw new QuicheException(QuicheError.QUICHE_ERR_DONE, "Connection was closed.");
            }

            dgramBuf = await dgramSendChannel!.Reader.ReadAsync(cancellationToken);
            unsafe
            {
                lock (this)
                {
                    fixed (byte* bufPtr = dgramBuf)
                    {
                        QuicheError errorCode = (QuicheError)NativePtr->DgramSend(bufPtr, (size_t)dgramBuf.Length);
                        QuicheException.ThrowIfError(errorCode);
                    }
                }
            }
        }
    }

    private async Task SendStreamAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosed)
            {
                throw new QuicheException(QuicheError.QUICHE_ERR_DONE, "Connection was closed.");
            }

            ulong streamId;
            QuicheStream? stream;
            ulong? streamIdOrNull = sendQueue
                .OrderByDescending(x => x.Value.Length)
                .Select(x => x.Key)
                .Cast<ulong?>()
                .FirstOrDefault();
            streamId = streamIdOrNull ?? 0UL;
            stream = streamIdOrNull.HasValue ?
                GetStream(streamId) : null;

            try
            {
                bool isConnectionEstablished, isInEarlyData;
                unsafe
                {
                    lock (this)
                    {
                        isConnectionEstablished = NativePtr->IsEstablished();
                        isInEarlyData = NativePtr->IsInEarlyData();
                    }
                }

                if (stream is null || (!isConnectionEstablished && !isInEarlyData))
                {
                    foreach (var str in streamMap.Values)
                    {
                        str.Flush();
                    }

                    await Task.Delay(75, cancellationToken);
                    continue;
                }

                if (!sendQueue.TryRemove(streamId, out byte[]? streamBuf) || streamBuf.Length == 0)
                {
                    stream.Flush();

                    await Task.Delay(75, cancellationToken);
                    continue;
                }

                long resultOrError, errorCode, bytesSent = 0;
                Lazy<bool> hasNotSentAllBytes;
                do
                {
                    unsafe
                    {
                        lock (this)
                        {
                            fixed (byte* bufPtr = streamBuf)
                            {
                                errorCode = (long)QuicheError.QUICHE_ERR_NONE;
                                resultOrError = (long)NativePtr->StreamSend(streamId,
                                    bufPtr + bytesSent, (nuint)(streamBuf.Length - bytesSent),
                                    bytesSent == streamBuf.Length && stream.IsShuttingDown,
                                    (ulong*)Unsafe.AsPointer(ref errorCode)
                                    );
                            }
                        }
                    }

                    hasNotSentAllBytes = new(() => (bytesSent += resultOrError) < streamBuf.Length);
                } while (resultOrError >= 0 && hasNotSentAllBytes.Value);

                sendQueue.AddOrUpdate(streamId,
                    key => streamBuf[(int)bytesSent..],
                    (key, buf) => [.. buf, .. streamBuf[(int)bytesSent..]]
                    );

                QuicheException.ThrowIfError((QuicheError)resultOrError);
            }
            catch (QuicheException ex)
            when (ex.ErrorCode == QuicheError.QUICHE_ERR_DONE)
            {
                if (IsClosed) { throw; }
                await Task.Delay(75, cancellationToken);
                continue;
            }
            catch (QuicheException ex)
            {
                establishedTcs.TrySetException(ex);
                throw;
            }
            catch (OperationCanceledException)
            {
                establishedTcs.TrySetCanceled(cancellationToken);
                throw;
            }
        }
    }

    private async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        byte[] packetBuf = new byte[QuicheLibrary.MAX_DATAGRAM_LEN];
        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosed)
            {
                throw new QuicheException(QuicheError.QUICHE_ERR_DONE, "Connection was closed.");
            }

            try
            {
                bool isConnEstablished, isInEarlyData;
                unsafe
                {
                    lock (this)
                    {
                        NativePtr->OnTimeout();

                        isConnEstablished = NativePtr->IsEstablished();
                        isInEarlyData = NativePtr->IsInEarlyData();
                    }
                }

                if (isConnEstablished)
                {
                    establishedTcs.TrySetResult();
                }

                ReadOnlyMemory<byte> nextPacket;
                if (!recvQueue.TryDequeue(out nextPacket) && !IsClosed)
                {
                    await Task.Delay(75, cancellationToken);
                    continue;
                }
                else if (IsClosed)
                {
                    throw new QuicheException(QuicheError.QUICHE_ERR_DONE, "Connection was closed.");
                }
                else
                {
                    nextPacket.CopyTo(packetBuf);
                }

                long resultOrError;
                unsafe
                {
                    lock (this)
                    {
                        var (to, to_len) = GetSocketAddress(socket.LocalEndPoint);
                        var (from, from_len) = GetSocketAddress(remoteEndPoint);

                        quiche_recv_info recvInfo = new quiche_recv_info
                        {
                            to = (sockaddr*)to.Pointer,
                            to_len = (size_t)to_len,

                            from = (sockaddr*)from.Pointer,
                            from_len = (size_t)from_len,
                        };

                        using (to)
                        using (from)
                        {
                            fixed (byte* bufPtr = packetBuf)
                            {
                                resultOrError = (long)NativePtr->Recv(
                                    bufPtr, (nuint)nextPacket.Length,
                                    (quiche_recv_info*)Unsafe.AsPointer(ref recvInfo)
                                    );
                            }
                        }
                    }
                }

                QuicheException.ThrowIfError((QuicheError)resultOrError);
            }
            catch (QuicheException ex)
            {
                establishedTcs.TrySetException(ex);
                throw;
            }
            catch (OperationCanceledException)
            {
                establishedTcs.TrySetCanceled(cancellationToken);
                throw;
            }
        }
    }

    private async Task ReceiveDatagramsAsync(CancellationToken cancellationToken)
    {
        byte[]? dgramBuf;
        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosed)
            {
                throw new QuicheException(QuicheError.QUICHE_ERR_DONE, "Connection was closed.");
            }

            unsafe
            {
                lock (this)
                {
                    if (NativePtr->DgramRecvQueueLen() == 0)
                    {
                        dgramBuf = null;
                    }
                    else
                    {
                        long dgramBufLen = (long)NativePtr->DgramRecvFrontLen();
                        dgramBuf = new byte[dgramBufLen];
                        fixed (byte* bufPtr = dgramBuf)
                        {
                            QuicheError errorCode = (QuicheError)NativePtr->DgramRecv(bufPtr, (size_t)dgramBuf.Length);
                            QuicheException.ThrowIfError(errorCode);
                        }
                    }
                }
            }

            if (dgramBuf is null)
            {
                await Task.Delay(75, cancellationToken);
            }
            else
            {
                await dgramRecvChannel!.Writer.WriteAsync(dgramBuf, cancellationToken);
            }
        }
    }

    private async Task ReceiveStreamAsync(CancellationToken cancellationToken)
    {
        byte[] streamBuf = new byte[QuicheLibrary.MAX_BUFFER_LEN];
        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosed)
            {
                throw new QuicheException(QuicheError.QUICHE_ERR_DONE, "Connection was closed.");
            }

            long streamIdOrNone;
            bool isConnEstablished, isInEarlyData;
            unsafe
            {
                lock (this)
                {
                    streamIdOrNone = NativePtr->StreamReadableNext();

                    isConnEstablished = NativePtr->IsEstablished();
                    isInEarlyData = NativePtr->IsInEarlyData();
                }
            }

            ulong streamId;
            QuicheStream stream;
            if (streamIdOrNone >= 0 && (isConnEstablished || isInEarlyData))
            {
                streamId = (ulong)streamIdOrNone;
                stream = GetStream(streamId);
            }
            else
            {
                await Task.Delay(75, cancellationToken);
                continue;
            }

            try
            {
                bool streamFinished = false;
                long recvCount = long.MaxValue;
                while (!streamFinished && recvCount > 0)
                {
                    long errorCode;
                    unsafe
                    {
                        lock (this)
                        {
                            fixed (byte* bufPtr = streamBuf)
                            {
                                errorCode = (long)QuicheError.QUICHE_ERR_NONE;
                                recvCount = (long)NativePtr->StreamRecv(streamId, bufPtr, (nuint)streamBuf.Length,
                                    (bool*)Unsafe.AsPointer(ref streamFinished), (ulong*)Unsafe.AsPointer(ref errorCode));
                            }
                        }
                    }

                    if (recvCount > 0)
                    {
                        await stream.ReceiveDataAsync(
                            streamBuf.AsMemory(0, (int)recvCount),
                            streamFinished, cancellationToken
                            );
                    }
                    else
                    {
                        QuicheException.ThrowIfError((QuicheError)recvCount);
                    }
                }
            }
            catch (QuicheException ex)
                when (ex.ErrorCode == QuicheError.QUICHE_ERR_DONE)
            {
                if (IsClosed) { throw; }
                await Task.Delay(75, cancellationToken);
                continue;
            }
            catch (QuicheException ex)
            {
                establishedTcs.TrySetException(ex);
                throw;
            }
            catch (OperationCanceledException)
            {
                establishedTcs.TrySetCanceled(cancellationToken);
                throw;
            }
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] packetBuf = new byte[QuicheLibrary.MAX_DATAGRAM_LEN];
            SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                packetBuf, remoteEndPoint, cancellationToken);
            recvQueue.Enqueue(packetBuf.AsMemory(0, result.ReceivedBytes));
        }
    }

    private QuicheStream GetStream(ulong streamId) =>
        streamMap.GetOrAdd(streamId, id =>
        {
            QuicheStream stream = new(this, id);
            if ((id & 1) != 0 ^ IsServer)
            {
                SpinWait.SpinUntil(() => streamChannel.Writer.TryWrite(stream));
            }
            return stream;
        });

    private bool IsStreamFinished(ulong streamId)
    {
        unsafe
        {
            lock (this)
            {
                return NativePtr is not null && NativePtr->StreamFinished(streamId);
            }
        }
    }

    public async Task<QuicheStream> CreateOutboundStreamAsync(Direction direction, CancellationToken cancellationToken = default)
    {
        ulong streamId, streamIdx = 0;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            streamId = (streamIdx++ << 2) | (ulong)direction | Convert.ToUInt64(IsServer);
            if (streamMap.ContainsKey(streamId))
            {
                await Task.Delay(75);
            }
            else
            {
                break;
            }
        } while (!cancellationToken.IsCancellationRequested);
        return GetStream(streamId);
    }

    public async Task<QuicheStream> AcceptInboundStreamAsync(CancellationToken cancellationToken = default)
    {
        return await streamChannel.Reader.ReadAsync(cancellationToken);
    }

    public bool TryReceiveDatagram([NotNullWhen(true)] out byte[]? dgramBuf)
    {
        if (!config.DatagramOptions.Enabled)
        {
            throw new QuicheException(QuicheError.QUICHE_ERR_INVALID_FRAME, "DATAGRAM frames are not enabled for this connection.");
        }
        else
        {
            return dgramRecvChannel!.Reader.TryRead(out dgramBuf);
        }
    }

    public async Task<byte[]> ReceiveDatagramAsync(CancellationToken cancellationToken = default)
    {
        if (!config.DatagramOptions.Enabled)
        {
            throw new QuicheException(QuicheError.QUICHE_ERR_INVALID_FRAME, "DATAGRAM frames are not enabled for this connection.");
        }
        else
        {
            return await dgramRecvChannel!.Reader.ReadAsync(cancellationToken);
        }
    }

    public bool TrySendDatagram(byte[] dgramBuf)
    {
        if (!config.DatagramOptions.Enabled)
        {
            throw new QuicheException(QuicheError.QUICHE_ERR_INVALID_FRAME, "DATAGRAM frames are not enabled for this connection.");
        }
        else if (MaxDatagramSize > 0 && dgramBuf.Length > MaxDatagramSize)
        {
            throw new ArgumentException($"Provided datagram buffer is too large. Use {nameof(MaxDatagramSize)} to get the maximum datagram size for this instance.", nameof(dgramBuf));
        }
        else
        {
            return dgramSendChannel!.Writer.TryWrite(dgramBuf);
        }
    }

    public async Task SendDatagramAsync(byte[] dgramBuf, CancellationToken cancellationToken = default)
    {
        if (!config.DatagramOptions.Enabled)
        {
            throw new QuicheException(QuicheError.QUICHE_ERR_INVALID_FRAME, "DATAGRAM frames are not enabled for this connection.");
        }
        else if (MaxDatagramSize > 0 && dgramBuf.Length > MaxDatagramSize)
        {
            throw new ArgumentException($"Provided datagram buffer is too large. Use {nameof(MaxDatagramSize)} to get the maximum datagram size for this instance.", nameof(dgramBuf));
        }
        else
        {
            await dgramSendChannel!.Writer.WriteAsync(dgramBuf, cancellationToken);
        }
    }

    public async Task WaitForShutdownAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested && !IsClosed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(75, cancellationToken);
        }
    }

    private bool disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        lock (this)
        {
            if (disposedValue)
            {
                return;
            }
            else
            {
                disposedValue = true;
            }
        }

        if (disposing)
        {
            foreach (var (_, stream) in streamMap)
            {
                stream.Dispose();
            }

            try
            {
                unsafe
                {
                    lock (this)
                    {
                        byte[] reasonBuf = Encoding.UTF8.GetBytes("Connection was implicitly closed for user initiated disposal.");
                        fixed (byte* reasonPtr = reasonBuf)
                        {
                            int errorResult = NativePtr->Close(true, 0x00, reasonPtr, (size_t)reasonBuf.Length);
                            QuicheException.ThrowIfError((QuicheError)errorResult, "Failed to close connection!");
                        }
                    }
                }

                Task.WaitAll(recvTask, sendTask, recvStreamTask, sendStreamTask);
            }
            catch (AggregateException ex)
            when (ex.InnerExceptions.All(x => x is
                QuicheException { ErrorCode: QuicheError.QUICHE_ERR_DONE } or
                OperationCanceledException
                ))
            { }
            catch (QuicheException ex)
            when (ex.ErrorCode == QuicheError.QUICHE_ERR_DONE)
            { }
            finally
            {
                cts.Cancel();
                cts.Dispose();

                recvQueue.Clear();
                sendQueue.Clear();

                streamMap.Clear();
                streamChannel.Writer.Complete();

                dgramRecvChannel?.Writer.Complete();
                dgramSendChannel?.Writer.Complete();
            }
        }

        unsafe
        {
            lock (this)
            {
                if (NativePtr is not null)
                {
                    NativePtr->Free();
                    NativePtr = null;
                }
            }
        }
    }

    ~QuicheConnection()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
