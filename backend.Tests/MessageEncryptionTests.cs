using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using YuChat.Infrastructure.Encryption;
using YuChat.Infrastructure.Options;

namespace YuChat.Api.Tests;

public sealed class MessageEncryptionTests
{
    private static IMessageEncryptionService CreateService(int activeVersion = 1, Dictionary<int, string>? keyRing = null)
    {
        var options = new EncryptionOptions
        {
            MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ActiveKeyVersion = activeVersion,
            KeyRing = keyRing ?? [],
        };

        return new AesGcmMessageEncryptionService(Options.Create(options));
    }

    [Fact]
    public void Encrypt_then_decrypt_returns_original_text()
    {
        var service = CreateService();
        var chatId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        const string plaintext = "こんにちは、世界! Hello, 世界 👋";

        var payload = service.Encrypt(plaintext, chatId, messageId);

        Assert.NotEqual(plaintext, Convert.ToBase64String(payload.CipherText));
        Assert.Equal(12, payload.Nonce.Length);
        Assert.Equal(16, payload.Tag.Length);
        Assert.Equal(1, payload.KeyVersion);

        var decrypted = service.Decrypt(payload.CipherText, payload.Nonce, payload.Tag, payload.KeyVersion, chatId, messageId);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Every_message_uses_a_unique_nonce()
    {
        var service = CreateService();
        var chatId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();

        var first = service.Encrypt("same text", chatId, messageId);
        var second = service.Encrypt("same text", chatId, messageId);

        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.CipherText, second.CipherText);
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected()
    {
        var service = CreateService();
        var chatId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        var payload = service.Encrypt("secret", chatId, messageId);

        payload.CipherText[0] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() =>
            service.Decrypt(payload.CipherText, payload.Nonce, payload.Tag, payload.KeyVersion, chatId, messageId));
    }

    [Fact]
    public void Tampered_tag_is_rejected()
    {
        var service = CreateService();
        var chatId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        var payload = service.Encrypt("secret", chatId, messageId);

        payload.Tag[0] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() =>
            service.Decrypt(payload.CipherText, payload.Nonce, payload.Tag, payload.KeyVersion, chatId, messageId));
    }

    [Fact]
    public void Message_moved_to_another_room_is_rejected()
    {
        var service = CreateService();
        var chatId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        var payload = service.Encrypt("secret", chatId, messageId);

        Assert.ThrowsAny<CryptographicException>(() =>
            service.Decrypt(payload.CipherText, payload.Nonce, payload.Tag, payload.KeyVersion, Guid.CreateVersion7(), messageId));
    }

    [Fact]
    public void Message_rebound_to_another_id_is_rejected()
    {
        var service = CreateService();
        var chatId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        var payload = service.Encrypt("secret", chatId, messageId);

        Assert.ThrowsAny<CryptographicException>(() =>
            service.Decrypt(payload.CipherText, payload.Nonce, payload.Tag, payload.KeyVersion, chatId, Guid.CreateVersion7()));
    }

    [Fact]
    public void Old_key_version_remains_decryptable_after_rotation()
    {
        var oldKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var newKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var oldService = new AesGcmMessageEncryptionService(Options.Create(new EncryptionOptions
        {
            MasterKey = oldKey,
            ActiveKeyVersion = 1,
        }));

        var chatId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        var payload = oldService.Encrypt("legacy", chatId, messageId);

        var rotatedService = new AesGcmMessageEncryptionService(Options.Create(new EncryptionOptions
        {
            MasterKey = newKey,
            ActiveKeyVersion = 2,
            KeyRing = new Dictionary<int, string> { [1] = oldKey },
        }));

        Assert.Equal(2, rotatedService.Encrypt("new", chatId, Guid.CreateVersion7()).KeyVersion);
        Assert.Equal("legacy", rotatedService.Decrypt(payload.CipherText, payload.Nonce, payload.Tag, payload.KeyVersion, chatId, messageId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("c2hvcnQ=")] // 5 bytes
    public void Invalid_master_key_fails_fast(string key)
    {
        Assert.Throws<InvalidOperationException>(() => new AesGcmMessageEncryptionService(Options.Create(new EncryptionOptions
        {
            MasterKey = key,
            ActiveKeyVersion = 1,
        })));
    }

    [Fact]
    public void Options_validator_rejects_short_key()
    {
        var result = new EncryptionOptions.Validator().Validate(null, new EncryptionOptions { MasterKey = "abc" });

        Assert.True(result.Failed);
    }
}
