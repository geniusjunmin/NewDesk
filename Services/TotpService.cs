using System;
using System.Security.Cryptography;
using System.Text;

namespace NewDesk.Services;

public static class TotpService
{
    public static (string Code, int RemainingSeconds) GenerateTotp(string? base32Secret)
    {
        if (string.IsNullOrWhiteSpace(base32Secret))
        {
            return ("------", 0);
        }

        try
        {
            byte[] secretBytes = Base32Decode(base32Secret);
            long timeStep = 30;
            long unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long counter = unixTime / timeStep;
            int remainingSeconds = (int)(timeStep - (unixTime % timeStep));

            byte[] counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(counterBytes);
            }

            using var hmac = new HMACSHA1(secretBytes);
            byte[] hash = hmac.ComputeHash(counterBytes);

            int offset = hash[^1] & 0x0F;
            int binaryCode = ((hash[offset] & 0x7F) << 24) |
                             ((hash[offset + 1] & 0xFF) << 16) |
                             ((hash[offset + 2] & 0xFF) << 8) |
                             (hash[offset + 3] & 0xFF);

            int code = binaryCode % 1000000;
            return (code.ToString("D6"), remainingSeconds);
        }
        catch
        {
            return ("------", 0);
        }
    }

    private static byte[] Base32Decode(string base32)
    {
        string cleaned = base32.Trim().TrimEnd('=').ToUpperInvariant();
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var bytes = new System.Collections.Generic.List<byte>();
        int bitBuffer = 0;
        int bitCount = 0;

        foreach (char c in cleaned)
        {
            int val = alphabet.IndexOf(c);
            if (val < 0) continue;

            bitBuffer = (bitBuffer << 5) | val;
            bitCount += 5;

            if (bitCount >= 8)
            {
                bitCount -= 8;
                bytes.Add((byte)((bitBuffer >> bitCount) & 0xFF));
            }
        }

        return bytes.ToArray();
    }
}
