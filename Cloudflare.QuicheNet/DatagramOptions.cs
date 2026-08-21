namespace Cloudflare.QuicheNet;

public record struct DatagramOptions
{
    public bool Enabled { get; init; }

    public int SendQueueLength { get; init; }

    public int ReceiveQueueLength { get; init; }
};
