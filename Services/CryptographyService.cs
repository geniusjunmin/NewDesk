using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NewDesk.Services;

public class EncryptedPayload
{
    public string? Salt { get; set; }
    public string? IV { get; set; }
    public string? EncryptedData { get; set; }
}

public static class CryptographyService
{
    private const int KeySize = 256;
    private const int BlockSize = 128;
    private const int Iterations = 10000;
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    public static EncryptedPayload Encrypt(string plainText, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);

        using var aes = Aes.Create();
        aes.KeySize = KeySize;
        aes.BlockSize = BlockSize;

        var key = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithm).GetBytes(KeySize / 8);
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var msEncrypt = new MemoryStream();
        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
        using (var swEncrypt = new StreamWriter(csEncrypt, Encoding.UTF8))
        {
            swEncrypt.Write(plainText);
        }

        return new EncryptedPayload
        {
            Salt = Convert.ToBase64String(salt),
            IV = Convert.ToBase64String(aes.IV),
            EncryptedData = Convert.ToBase64String(msEncrypt.ToArray())
        };
    }

    public static string Decrypt(EncryptedPayload payload, string password)
    {
        byte[] salt = Convert.FromBase64String(payload.Salt!);
        byte[] iv = Convert.FromBase64String(payload.IV!);
        byte[] cipherText = Convert.FromBase64String(payload.EncryptedData!);

        using var aes = Aes.Create();
        aes.KeySize = KeySize;
        aes.BlockSize = BlockSize;

        var key = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithm).GetBytes(KeySize / 8);
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var msDecrypt = new MemoryStream(cipherText);
        using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
        using var srDecrypt = new StreamReader(csDecrypt, Encoding.UTF8);

        return srDecrypt.ReadToEnd();
    }
}
