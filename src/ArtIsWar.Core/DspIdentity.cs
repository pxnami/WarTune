using System.Security.Cryptography;
using System.Text;

namespace ArtIsWar.Core;

public static class DspIdentity
{
    public static int Token(string endpointId) => BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(endpointId)), 0) & 0xFFFFF;
    public static string MappingName(string endpointId) => $"Global\\ArtIsWarAudio-DSP-{Token(endpointId):X8}";
}
