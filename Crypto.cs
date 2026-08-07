using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class Crypto
{
    // Default key and IV for compatibility, ensure they match the original implementation.
    private static readonly byte[] DefaultKey = Encoding.ASCII.GetBytes("12345678");
    private static readonly byte[] DefaultIV = Encoding.ASCII.GetBytes("87654321");
public static string jiem(string mima)
{
	mima = remzifu(mima);
	mima = jiemi(mima);
	mima = jiemi(mima);
	return mima;
}
private static string remzifu(string mima)
{
	string text = "";
	for (int i = 20; i < mima.Length - 20; i++)
	{
		text += mima[i];
	}
	return text;
}
private static int su(int k)
{
	int num = 0;
	int result = 0;
	for (num = 2; num < k && k % num != 0; num++)
	{
	}
	if (k == num)
	{
		result = 1;
	}
	return result;
}
private static string jiemi(string mima)
{
	string text = "";
	for (int i = 0; i < mima.Length; i++)
	{
		if (i % 2 == 0)
		{
			if (su(i) == 1)
			{
				int num = mima[i] - 4;
				char c = (char)num;
				text += c;
			}
			else
			{
				int num = mima[i] - 3;
				char c = (char)num;
				text += c;
			}
		}
		else if (su(i) == 1)
		{
			int num = mima[i] - 2;
			char c = (char)num;
			text += c;
		}
		else
		{
			int num = mima[i] - 1;
			char c = (char)num;
			text += c;
		}
	}
	return text;
}
    public static string DESEncrypt(string plainText, string key, string iv)
    {
        try
        {
            using (var des = DES.Create())
            {
                byte[] keyBytes = Encoding.UTF8.GetBytes(key.PadRight(8, ' ').Substring(0, 8));
                byte[] ivBytes = Encoding.UTF8.GetBytes(iv.PadRight(8, ' ').Substring(0, 8));
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

                des.Key = keyBytes;
                des.IV = ivBytes;
                des.Mode = CipherMode.CBC;
                des.Padding = PaddingMode.PKCS7;

                using (var ms = new MemoryStream())
                using (var cs = new CryptoStream(ms, des.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(plainBytes, 0, plainBytes.Length);
                    cs.FlushFinalBlock();
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string DESDecrypt(string base64Text, string key, string iv)
    {
        if (string.IsNullOrEmpty(base64Text)) return "";

        string text = base64Text;
        if (text.Length < 16)
        {
            return "";
        }
        for (int i = 0; i < 8; i++)
        {
            if (text.Length > i + 1)
            {
                text = text.Substring(0, i + 1) + text.Substring(i + 2);
            }
        }
        
        string encryptedValue = text;
        string localKey = (key + "12345678").Substring(0, 8);
        string localIV = (iv + "12345678").Substring(0, 8);

        try
        {
            using (var des = DES.Create())
            {
                des.Key = Encoding.UTF8.GetBytes(localKey);
                des.IV = Encoding.UTF8.GetBytes(localIV);
                des.Mode = CipherMode.CBC;
                des.Padding = PaddingMode.PKCS7;

                using (var ms = new MemoryStream())
                using (var cs = new CryptoStream(ms, des.CreateDecryptor(), CryptoStreamMode.Write))
                {
                    byte[] array = Convert.FromBase64String(encryptedValue);
                    cs.Write(array, 0, array.Length);
                    cs.FlushFinalBlock();
                    string decoded = Encoding.UTF8.GetString(ms.ToArray());
                    return jiem(decoded);
                }
            }
        }
        catch (Exception)
        {
            return "";
        }
    }
}
