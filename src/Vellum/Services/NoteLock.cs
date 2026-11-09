using System.Security.Cryptography;
using System.Text;

namespace Vellum.Services;

/// <summary>
/// The key of a note under a password, kept in memory only while its tab is open.
/// The password itself is never stored anywhere.
/// </summary>
public sealed class NoteKey
{
    internal NoteKey(byte[] salt, byte[] key)
    {
        Salt = salt;
        Key = key;
    }

    internal byte[] Salt { get; }
    internal byte[] Key { get; }
}

/// <summary>
/// Password-protected notes: AES-256-GCM, with the key made from the password by PBKDF2-SHA256.
/// A locked note file looks like "vellum-locked:1:" followed by base64 of salt + nonce + tag + ciphertext.
/// Without the password the text cannot be read, also not by us.
/// </summary>
public static class NoteLock
{
    private const string Header = "vellum-locked:1:";
    private const int Iterations = 310_000;
    private const int SaltSize = 16, NonceSize = 12, TagSize = 16, KeySize = 32;

    public static bool IsLocked(string? fileText) => fileText?.StartsWith(Header, StringComparison.Ordinal) == true;

    /// <summary>A new key for a password, with a fresh random salt. Slow on purpose (about a quarter of a second).</summary>
    public static NoteKey NewKey(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new NoteKey(salt, Derive(password, salt));
    }

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeySize);

    public static string Encrypt(string text, NoteKey key)
    {
        byte[] plain = Encoding.UTF8.GetBytes(text);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[TagSize];
        using (var aes = new AesGcm(key.Key, TagSize))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }
        CryptographicOperations.ZeroMemory(plain);

        byte[] all = new byte[SaltSize + NonceSize + TagSize + cipher.Length];
        key.Salt.CopyTo(all, 0);
        nonce.CopyTo(all, SaltSize);
        tag.CopyTo(all, SaltSize + NonceSize);
        cipher.CopyTo(all, SaltSize + NonceSize + TagSize);
        return Header + Convert.ToBase64String(all);
    }

    /// <summary>The text of a locked note and its key, or null when the password is wrong (or the file is damaged).</summary>
    public static (string Text, NoteKey Key)? TryDecrypt(string fileText, string password)
    {
        if (!IsLocked(fileText)) return null;
        try
        {
            byte[] all = Convert.FromBase64String(fileText[Header.Length..].Trim());
            if (all.Length < SaltSize + NonceSize + TagSize) return null;
            byte[] salt = all[..SaltSize];
            byte[] nonce = all[SaltSize..(SaltSize + NonceSize)];
            byte[] tag = all[(SaltSize + NonceSize)..(SaltSize + NonceSize + TagSize)];
            byte[] cipher = all[(SaltSize + NonceSize + TagSize)..];

            byte[] key = Derive(password, salt);
            byte[] plain = new byte[cipher.Length];
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, cipher, tag, plain);
            }
            string text = Encoding.UTF8.GetString(plain);
            CryptographicOperations.ZeroMemory(plain);
            return (text, new NoteKey(salt, key));
        }
        catch (Exception)
        {
            return null; // AuthenticationTagMismatchException: wrong password
        }
    }

    /// <summary>True when the password gives this same key (asked again before the password is removed).</summary>
    public static bool Matches(NoteKey key, string password) =>
        CryptographicOperations.FixedTimeEquals(Derive(password, key.Salt), key.Key);
}
