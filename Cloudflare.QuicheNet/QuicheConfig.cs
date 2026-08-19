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

    public long AcknowledgementDelayExponent
    {
        set
        {
            unsafe
            {
                NativePtr->SetAckDelayExponent((ulong)value);
            }
        }
    }

    public long ActiveConnectionIdLimit
    {
        set
        {
            unsafe
            {
                NativePtr->SetActiveConnectionIdLimit((ulong)value);
            }
        }
    }

    public QuicheCcAlgorithm CcAlgorithm
    {
        set
        {
            unsafe
            {
                NativePtr->SetCcAlgorithm((size_t)(int)value);
            }
        }
    }

    public int InitialCongestionWindowPackets
    {
        set
        {
            unsafe
            {
                NativePtr->SetInitialCongestionWindowPackets((nuint)value);
            }
        }
    }

    public bool IsActiveMigrationDisabled
    {
        set
        {
            unsafe
            {
                NativePtr->SetDisableActiveMigration(value);
            }
        }
    }

    public bool IsHyStartEnabled
    {
        set
        {
            unsafe
            {
                NativePtr->EnableHystart(value);
            }
        }
    }

    public bool IsPacingEnabled
    {
        set
        {
            unsafe
            {
                NativePtr->EnablePacing(value);
            }
        }
    }

    public long MaxAcknowledgementDelay
    {
        set
        {
            unsafe
            {
                NativePtr->SetMaxAckDelay((ulong)value);
            }
        }
    }

    public int MaxAmplificationFactor
    {
        set
        {
            unsafe
            {
                NativePtr->SetMaxAmplificationFactor((nuint)value);
            }
        }
    }

    public long MaxIdleTimeout
    {
        set
        {
            unsafe
            {
                NativePtr->SetMaxIdleTimeout((ulong)value);
            }
        }
    }

    public long MaxInitialBidiStreams
    {
        set
        {
            unsafe
            {
                NativePtr->SetInitialMaxStreamsBidi((ulong)value);
            }
        }
    }

    public long MaxInitialDataSize
    {
        set
        {
            unsafe
            {
                NativePtr->SetInitialMaxData((ulong)value);
            }
        }
    }

    public long MaxInitialLocalBidiStreamDataSize
    {
        set
        {
            unsafe
            {
                NativePtr->SetInitialMaxStreamDataBidiLocal((ulong)value);
            }
        }
    }

    public long MaxInitialRemoteBidiStreamDataSize
    {
        set
        {
            unsafe
            {
                NativePtr->SetInitialMaxStreamDataBidiRemote((ulong)value);
            }
        }
    }

    public long MaxInitialUniStreamDataSize
    {
        set
        {
            unsafe
            {
                NativePtr->SetInitialMaxStreamDataUni((ulong)value);
            }
        }
    }

    public long MaxInitialUniStreams
    {
        set
        {
            unsafe
            {
                NativePtr->SetInitialMaxStreamsUni((ulong)value);
            }
        }
    }

    public long MaxPacingRate
    {
        set
        {
            unsafe
            {
                NativePtr->SetMaxPacingRate((ulong)value);
            }
        }
    }

    public int MaxReceiveUdpPayloadSize
    {
        set
        {
            unsafe
            {
                NativePtr->SetMaxRecvUdpPayloadSize((nuint)value);
            }
        }
    }

    public int MaxSendUdpPayloadSize
    {
        set
        {
            unsafe
            {
                NativePtr->SetMaxSendUdpPayloadSize((nuint)value);
            }
        }
    }

    public bool ShouldDiscoverPathMtu
    {
        set
        {
            unsafe
            {
                NativePtr->DiscoverPmtu(value);
            }
        }
    }

    public bool ShouldSendGrease
    {
        set
        {
            unsafe
            {
                NativePtr->Grease(value);
            }
        }
    }

    public bool ShouldVerifyPeer
    {
        set
        {
            unsafe
            {
                NativePtr->VerifyPeer(value);
            }
        }
    }    

    public QuicheConfig(
        bool isEarlyDataEnabled = false,
        bool shouldLogKeys = false
        )
    {
        unsafe
        {
            NativePtr = quiche_config_new(PROTOCOL_VERSION);

            if (isEarlyDataEnabled)
            {
                NativePtr->EnableEarlyData();
            }

            if (shouldLogKeys)
            {
                NativePtr->LogKeys();
            }
        }
    }

    public void LoadCertificateChainFromPemFile(string filePath)
    {
        unsafe
        {
            fixed (byte* filePathPtr = Encoding.UTF8.GetBytes([.. filePath.ToCharArray(), '\u0000']))
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
            fixed (byte* filePathPtr = Encoding.UTF8.GetBytes([.. filePath.ToCharArray(), '\u0000']))
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
            fixed (byte* pathPtr = Encoding.UTF8.GetBytes([.. path.ToCharArray(), '\u0000']))
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
            fixed (byte* filePathPtr = Encoding.UTF8.GetBytes([.. filePath.ToCharArray(), '\u0000']))
            {
                QuicheException.ThrowIfError(
                    (QuicheError)NativePtr->LoadVerifyLocationsFromFile(filePathPtr),
                    "Failed to load trusted CA locations from provided file!"
                    );
            }
        }
    }

    public void SetApplicationProtocols(params string[] protos)
    {
        List<byte> protoList = new();
        foreach (string proto in protos)
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
    }

    public void SetTicketKey(byte[] keyBytes)
    {
        unsafe
        {
            fixed (byte* keyBytesPtr = keyBytes)
            {
                QuicheException.ThrowIfError((QuicheError)NativePtr->
                    SetTicketKey(keyBytesPtr, (nuint)keyBytes.Length),
                    "Failed to set ticket key contents for this instance.");
            }
        }
    }

    public void EnableDatagram(bool enabled, int sendQueueLength, int receiveQueueLength) 
    {
        unsafe
        {
            NativePtr->EnableDgram(enabled, (size_t)sendQueueLength, (size_t)receiveQueueLength);
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
