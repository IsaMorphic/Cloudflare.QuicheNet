using System.Text;

using Cloudflare.Quiche;
using static Cloudflare.Quiche.NativeMethods;
using static Cloudflare.QuicheNet.QuicheLibrary;

namespace Cloudflare.QuicheNet;

public class QuicheConfig : IDisposable
{
    // quiche_config handle

    internal unsafe quiche_config* NativePtr { get; private set; }

    // quiche_config properties    

    private readonly List<string> applicationProtocols;
    public IReadOnlyList<string> ApplicationProtocols
    {
        get => applicationProtocols;
        set
        {
            List<byte> protoList = new();
            foreach (string proto in value)
            {
                protoList.AddRange([(byte)proto.Length, .. Encoding.UTF8.GetBytes(proto)]);
            }

            unsafe
            {
                fixed (byte* protosPtr = protoList.ToArray())
                {
                    QuicheException.ThrowIfError((QuicheError)NativePtr->
                        SetApplicationProtos(protosPtr, (nuint)protoList.Count),
                        "Failed to set application protocols for this instance.");
                }
            }

            applicationProtocols.Clear();
            applicationProtocols.AddRange(value);
        }
    }

    private ReadOnlyMemory<byte> ticketKey;
    public ReadOnlyMemory<byte> TicketKey
    {
        get => ticketKey;
        set
        {
            byte[] keyBytes = value.ToArray();
            unsafe
            {
                fixed (byte* keyBytesPtr = keyBytes)
                {
                    QuicheException.ThrowIfError((QuicheError)NativePtr->
                        SetTicketKey(keyBytesPtr, (nuint)keyBytes.Length),
                        "Failed to set ticket key contents for this instance.");
                }
            }

            ticketKey = keyBytes;
        }
    }

    private DatagramOptions datagramOptions;
    public DatagramOptions DatagramOptions
    {
        get => datagramOptions;
        set
        {
            datagramOptions = value;
            unsafe
            {
                NativePtr->EnableDgram(datagramOptions.Enabled,
                    (size_t)datagramOptions.SendQueueLength,
                    (size_t)datagramOptions.ReceiveQueueLength);
            }
        }
    }

    private long acknowledgementDelayExponent;
    public long AcknowledgementDelayExponent
    {
        get => acknowledgementDelayExponent;
        set
        {
            acknowledgementDelayExponent = value;
            unsafe
            {
                NativePtr->SetAckDelayExponent((ulong)acknowledgementDelayExponent);
            }
        }
    }

    private long activeConnectionIdLimit;
    public long ActiveConnectionIdLimit
    {
        get => activeConnectionIdLimit;
        set
        {
            activeConnectionIdLimit = value;
            unsafe
            {
                NativePtr->SetActiveConnectionIdLimit((ulong)activeConnectionIdLimit);
            }
        }
    }

    private QuicheCcAlgorithm ccAlgorithm;
    public QuicheCcAlgorithm CcAlgorithm
    {
        set
        {
            ccAlgorithm = value;
            unsafe
            {
                NativePtr->SetCcAlgorithm((size_t)(int)ccAlgorithm);
            }
        }
    }

    private int initialCongestionWindowPackets;
    public int InitialCongestionWindowPackets
    {
        get => initialCongestionWindowPackets;
        set
        {
            initialCongestionWindowPackets = value;
            unsafe
            {
                NativePtr->SetInitialCongestionWindowPackets((nuint)initialCongestionWindowPackets);
            }
        }
    }

    private bool isActiveMigrationDisabled;
    public bool IsActiveMigrationDisabled
    {
        get => isActiveMigrationDisabled;
        set
        {
            isActiveMigrationDisabled = value;
            unsafe
            {
                NativePtr->SetDisableActiveMigration(isActiveMigrationDisabled);
            }
        }
    }

    private bool isHyStartEnabled;
    public bool IsHyStartEnabled
    {
        get => isHyStartEnabled;
        set
        {
            isHyStartEnabled = value;
            unsafe
            {
                NativePtr->EnableHystart(isHyStartEnabled);
            }
        }
    }

    private bool isPacingEnabled;
    public bool IsPacingEnabled
    {
        get => isPacingEnabled;
        set
        {
            isPacingEnabled = value;
            unsafe
            {
                NativePtr->EnablePacing(isPacingEnabled);
            }
        }
    }

    public bool IsEarlyDataEnabled { get; }

    private long maxAcknowledgmentDelay;
    public long MaxAcknowledgementDelay
    {
        get => maxAcknowledgmentDelay;
        set
        {
            maxAcknowledgmentDelay = value;
            unsafe
            {
                NativePtr->SetMaxAckDelay((ulong)maxAcknowledgmentDelay);
            }
        }
    }

    private int maxAmplificationFactor;
    public int MaxAmplificationFactor
    {
        get => maxAmplificationFactor;
        set
        {
            maxAmplificationFactor = value;
            unsafe
            {
                NativePtr->SetMaxAmplificationFactor((nuint)maxAmplificationFactor);
            }
        }
    }

    private long maxIdleTimeout;
    public long MaxIdleTimeout
    {
        get => maxIdleTimeout;
        set
        {
            maxIdleTimeout = value;
            unsafe
            {
                NativePtr->SetMaxIdleTimeout((ulong)maxIdleTimeout);
            }
        }
    }

    private long maxInitialBidiStreams;
    public long MaxInitialBidiStreams
    {
        get => maxInitialBidiStreams;
        set
        {
            maxInitialBidiStreams = value;
            unsafe
            {
                NativePtr->SetInitialMaxStreamsBidi((ulong)maxInitialBidiStreams);
            }
        }
    }

    private long maxInitialDataSize;
    public long MaxInitialDataSize
    {
        get => maxInitialDataSize;
        set
        {
            maxInitialDataSize = value;
            unsafe
            {
                NativePtr->SetInitialMaxData((ulong)maxInitialDataSize);
            }
        }
    }

    private long maxInitialLocalBidiStreamDataSize;
    public long MaxInitialLocalBidiStreamDataSize
    {
        get => maxInitialLocalBidiStreamDataSize;
        set
        {
            maxInitialLocalBidiStreamDataSize = value;
            unsafe
            {
                NativePtr->SetInitialMaxStreamDataBidiLocal((ulong)maxInitialLocalBidiStreamDataSize);
            }
        }
    }

    private long maxInitialRemoteBidiStreamDataSize;
    public long MaxInitialRemoteBidiStreamDataSize
    {
        get => maxInitialRemoteBidiStreamDataSize;
        set
        {
            maxInitialRemoteBidiStreamDataSize = value;
            unsafe
            {
                NativePtr->SetInitialMaxStreamDataBidiRemote((ulong)maxInitialRemoteBidiStreamDataSize);
            }
        }
    }

    private long maxInitialUniStreamDataSize;
    public long MaxInitialUniStreamDataSize
    {
        get => maxInitialUniStreamDataSize;
        set
        {
            maxInitialUniStreamDataSize = value;
            unsafe
            {
                NativePtr->SetInitialMaxStreamDataUni((ulong)maxInitialUniStreamDataSize);
            }
        }
    }

    private long maxInitialUniStreams;
    public long MaxInitialUniStreams
    {
        get => maxInitialUniStreams;
        set
        {
            maxInitialUniStreams = value;
            unsafe
            {
                NativePtr->SetInitialMaxStreamsUni((ulong)maxInitialUniStreams);
            }
        }
    }

    private long maxPacingRate;
    public long MaxPacingRate
    {
        get => maxPacingRate;
        set
        {
            maxPacingRate = value;
            unsafe
            {
                NativePtr->SetMaxPacingRate((ulong)maxPacingRate);
            }
        }
    }

    private int maxReceiveUdpPayloadSize;
    public int MaxReceiveUdpPayloadSize
    {
        get => maxReceiveUdpPayloadSize;
        set
        {
            maxReceiveUdpPayloadSize = value;
            unsafe
            {
                NativePtr->SetMaxRecvUdpPayloadSize((nuint)maxReceiveUdpPayloadSize);
            }
        }
    }

    private int maxSendUdpPayloadSize;
    public int MaxSendUdpPayloadSize
    {
        get => maxSendUdpPayloadSize;
        set
        {
            maxSendUdpPayloadSize = value;
            unsafe
            {
                NativePtr->SetMaxSendUdpPayloadSize((nuint)maxSendUdpPayloadSize);
            }
        }
    }

    private bool shouldDiscoverPathMtu;
    public bool ShouldDiscoverPathMtu
    {
        get => shouldDiscoverPathMtu;
        set
        {
            shouldDiscoverPathMtu = value;
            unsafe
            {
                NativePtr->DiscoverPmtu(shouldDiscoverPathMtu);
            }
        }
    }

    private bool shouldSendGrease;
    public bool ShouldSendGrease
    {
        get => shouldSendGrease;
        set
        {
            shouldSendGrease = value;
            unsafe
            {
                NativePtr->Grease(shouldSendGrease);
            }
        }
    }

    private bool shouldVerifyPeer;
    public bool ShouldVerifyPeer
    {
        get => shouldVerifyPeer;
        set
        {
            shouldVerifyPeer = value;
            unsafe
            {
                NativePtr->VerifyPeer(shouldVerifyPeer);
            }
        }
    }

    public bool ShouldLogKeys { get; }

    public QuicheConfig(
        bool isEarlyDataEnabled = false,
        bool shouldLogKeys = false
        )
    {
        unsafe
        {
            NativePtr = quiche_config_new(PROTOCOL_VERSION);

            if (IsEarlyDataEnabled = isEarlyDataEnabled)
            {
                NativePtr->EnableEarlyData();
            }

            if (ShouldLogKeys = shouldLogKeys)
            {
                NativePtr->LogKeys();
            }
        }

        applicationProtocols = new();
        ticketKey = ReadOnlyMemory<byte>.Empty;
    }

    public void LoadCertificateChainFromPemFile(string filePath)
    {
        unsafe
        {
            fixed (byte* filePathPtr = Encoding.UTF8.GetBytes([.. filePath, '\0']))
            {
                QuicheException.ThrowIfError(
                    (QuicheError)NativePtr->LoadCertChainFromPemFile(filePathPtr),
                    "Failed to load certificate chain from provided PEM file!"
                    );
            }
        }
    }

    public void LoadPrivateKeyFromPemFile(string filePath)
    {
        unsafe
        {
            fixed (byte* filePathPtr = Encoding.UTF8.GetBytes([.. filePath, '\0']))
            {
                QuicheException.ThrowIfError(
                    (QuicheError)NativePtr->LoadPrivKeyFromPemFile(filePathPtr),
                    "Failed to load private key from provided PEM file!"
                    );
            }
        }
    }

    public void LoadVerifyLocationsFromDirectory(string path)
    {
        unsafe
        {
            fixed (byte* pathPtr = Encoding.UTF8.GetBytes([.. path, '\0']))
            {
                QuicheException.ThrowIfError(
                    (QuicheError)NativePtr->LoadVerifyLocationsFromDirectory(pathPtr),
                    "Failed to load trusted CA locations from provided directory!"
                    );
            }
        }
    }

    public void LoadVerifyLocationsFromFile(string filePath)
    {
        unsafe
        {
            fixed (byte* filePathPtr = Encoding.UTF8.GetBytes([.. filePath, '\0']))
            {
                QuicheException.ThrowIfError(
                    (QuicheError)NativePtr->LoadVerifyLocationsFromFile(filePathPtr),
                    "Failed to load trusted CA locations from provided file!"
                    );
            }
        }
    }

    #region IDisposable

    private bool disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        unsafe
        {
            if (!disposedValue)
            {
                NativePtr->Free();
                NativePtr = null;

                disposedValue = true;
            }
        }
    }

    ~QuicheConfig()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion
}
