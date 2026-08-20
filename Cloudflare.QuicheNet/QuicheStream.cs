using System.Buffers;
using System.IO.Pipelines;

namespace Cloudflare.QuicheNet
{
    public class QuicheStream : Stream
    {
        public enum Shutdown
        {
            Read = 0,
            Write = 1,
        }

        public enum Direction
        {
            Bidirectional = 0x0,
            Unidirectional = 0x2,
        }

        private readonly Pipe? recvPipe, sendPipe;

        private readonly QuicheConnection conn;

        private readonly ulong streamId;

        private bool disposedValue;

        public override bool CanRead => recvPipe is not null;

        public override bool CanWrite => sendPipe is not null;

        public override bool CanSeek => false;

        public override long Position
        {
            get => throw new NotSupportedException("This stream cannot be seeked.");
            set => throw new NotSupportedException("This stream cannot be seeked.");
        }

        public override long Length => throw new NotSupportedException("This stream cannot have its length set or changed.");

        internal bool IsShuttingDown => disposedValue;

        internal QuicheStream(QuicheConnection conn, ulong streamId)
        {
            this.conn = conn;
            this.streamId = streamId;

            bool isPeerInitiated = ((streamId & 1) == 0) ^ conn.IsServer;
            bool isBidirectional = (streamId & 2) == 0;

            if (!isPeerInitiated || isBidirectional)
            {
                recvPipe = new Pipe();
            }

            if (isPeerInitiated || isBidirectional)
            {
                sendPipe = new Pipe();
            }
        }

        internal async Task ReceiveDataAsync(ReadOnlyMemory<byte> bufIn, bool finished, CancellationToken cancellationToken)
        {
            if (recvPipe is null)
            {
                throw new NotSupportedException();
            }
            else
            {
                Memory<byte> memory = recvPipe.Writer.GetMemory(bufIn.Length);
                bufIn.CopyTo(memory); recvPipe.Writer.Advance(bufIn.Length);

                await recvPipe.Writer.FlushAsync(cancellationToken);

                if (finished)
                {
                    await recvPipe.Writer.CompleteAsync();
                }
            }
        }

        public override void Flush()
        {
            while (sendPipe is not null && sendPipe.Reader.TryRead(out ReadResult result))
            {
                conn.sendQueue.AddOrUpdate(streamId,
                    key => result.Buffer.ToArray(),
                    (key, buf) => [.. buf, .. result.Buffer.ToArray()]
                    );
                sendPipe.Reader.AdvanceTo(result.Buffer.End);
                if (result.IsCompleted) break;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (recvPipe is not null)
            {
                int bytesTotal = 0;
                while (bytesTotal < count)
                {
                    if (recvPipe.Reader.TryRead(out ReadResult result))
                    {
                        int bytesRead = (int)Math.Min(result.Buffer.Length, count - bytesTotal);

                        result.Buffer.Slice(result.Buffer.Start, bytesRead).CopyTo(buffer.AsSpan(offset + bytesTotal, bytesRead));
                        recvPipe.Reader.AdvanceTo(result.Buffer.GetPosition(bytesRead));

                        bytesTotal += bytesRead;
                        if (result.IsCompleted) break;
                    }
                    else
                    {
                        break;
                    }
                }

                return bytesTotal;
            }
            else
            {
                throw new NotSupportedException("This stream is not readable.");
            }
        }

        public override async void Write(byte[] buffer, int offset, int count)
        {
            if (sendPipe is not null)
            {
                Memory<byte> memory = sendPipe.Writer.GetMemory(count);
                buffer.AsMemory(offset, count).CopyTo(memory);
                sendPipe.Writer.Advance(count);

                await sendPipe.Writer.FlushAsync();
            }
            else
            {
                throw new NotSupportedException("This stream is not writable.");
            }
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException("This stream cannot be seeked.");

        public override void SetLength(long value) =>
            throw new NotSupportedException("This stream cannot have its length set or changed.");

        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                disposedValue = true;

                if (disposing)
                {
                    if (recvPipe is not null)
                    {
                        recvPipe.Writer.Complete();
                    }

                    if (sendPipe is not null)
                    {
                        sendPipe.Writer.Complete();
                    }

                    Flush();
                }
            }

            base.Dispose(disposing);
        }
    }
}
