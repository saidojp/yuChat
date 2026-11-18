using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using YuChat.Infrastructure.Options;

namespace YuChat.Infrastructure.Encryption;

public sealed record EncryptedPayload(byte[] CipherText, byte[] Nonce, byte[] Tag, int KeyVersion);

public sealed record DecryptedPayload(string PlainText);

public interface IMessageEncryptionService
{
    EncryptedPayload Encrypt(string plainText, Guid chatRoomId, Guid messageId);

    string Decrypt(byte[] cipherText, byte[] nonce, byte[] tag, int keyVersion, Guid chatRoomId, Guid messageId);
}

/// <summary>
/// AES-256-GCM message encryption with associated data binding a ciphertext to a
/// specific message inside a specific room. Switching rows in the database therefore
/// makes decryption fail instead of silently returning a valid message.
/// </summary>
public sealed class AesGcmMessageEncryptionService : IMessageEncryptionService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly Dictionary<int, AesGcm> _keys;
    private readonly int _activeKeyVersion;

    public AesGcmMessageEncryptionService(IOptions<EncryptionOptions> options)
    {
        var value = options.Value;

        _activeKeyVersion = value.ActiveKeyVersion;

        _keys = new Dictionary<int, AesGcm>();

        foreach (var (version, base64) in value.KeyRing)
        {
            if (!EncryptionOptions.Validator.TryDecode(base64, out var key))
            {
                throw new InvalidOperationException($"Encryption key version {version} is invalid.");
            }

            _keys[version] = new AesGcm(key, TagSize);
        }

        // The active key always wins over a stale KeyRing entry.
        if (!EncryptionOptions.Validator.TryDecode(value.MasterKey, out var masterKey))
        {
            throw new InvalidOperationException("Encryption master key is invalid.");
        }

        _keys[_activeKeyVersion] = new AesGcm(masterKey, TagSize);
    }

    public EncryptedPayload Encrypt(string plainText, Guid chatRoomId, Guid messageId)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherText = new byte[plainBytes.Length];
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var associatedData = BuildAssociatedData(chatRoomId, messageId);

        try
        {
            _keys[_activeKeyVersion].Encrypt(nonce, plainBytes, cipherText, tag, associatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            CryptographicOperations.ZeroMemory(associatedData);
        }

        return new EncryptedPayload(cipherText, nonce, tag, _activeKeyVersion);
    }

    public string Decrypt(byte[] cipherText, byte[] nonce, byte[] tag, int keyVersion, Guid chatRoomId, Guid messageId)
    {
        ArgumentNullException.ThrowIfNull(cipherText);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(tag);

        if (nonce.Length != NonceSize)
        {
            throw new CryptographicException("Invalid nonce length.");
        }

        if (tag.Length != TagSize)
        {
            throw new CryptographicException("Invalid authentication tag length.");
        }

        if (!_keys.TryGetValue(keyVersion, out var aes))
        {
            throw new CryptographicException($"Unknown encryption key version {keyVersion}.");
        }

        var plainBytes = new byte[cipherText.Length];
        var associatedData = BuildAssociatedData(chatRoomId, messageId);

        try
        {
            aes.Decrypt(nonce, cipherText, tag, plainBytes, associatedData);
            return Encoding.UTF8.GetString(plainBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    private static byte[] BuildAssociatedData(Guid chatRoomId, Guid messageId)
    {
        var buffer = new byte[32];
        chatRoomId.TryWriteBytes(buffer.AsSpan(0, 16));
        messageId.TryWriteBytes(buffer.AsSpan(16, 16));
        return buffer;
    }
}
