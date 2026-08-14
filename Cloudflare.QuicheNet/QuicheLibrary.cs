using static Cloudflare.Quiche.NativeMethods;

namespace Cloudflare.QuicheNet;

public static class QuicheLibrary
{
    public const uint PROTOCOL_VERSION = 0x00000001;

    public const int MAX_CONN_ID_LEN = 20;

    public const int MIN_CLIENT_INITIAL_LEN = 1200;

    public const int MAX_DATAGRAM_LEN = 6115;

    public const int MAX_BUFFER_LEN = 1024 * 1024;

    public static string VersionCode
    {
        get
        {
            unsafe
            {
                sbyte* versionCodePtr = (sbyte*)quiche_version();
                return new string(versionCodePtr);
            }
        }
    }
}
