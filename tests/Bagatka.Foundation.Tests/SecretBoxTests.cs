using System;
using System.Security.Cryptography;
using Bagatka.Foundation.Modules;
using Xunit;

namespace Bagatka.Foundation.Tests;

public sealed class SecretBoxTests
{
    private static readonly EncryptionSettings Encryption = new EncryptionSettings("0123456789abcdef0123456789abcdef");
    private static readonly Guid Row = new Guid("0199a7a4-5b9e-7c3d-8e2f-1a2b3c4d5e6f");
    private static readonly Guid OtherRow = new Guid("0199a7a4-5b9e-7c3d-8e2f-6f5e4d3c2b1a");

    [Fact]
    public void A_box_opens_what_it_sealed_for_the_same_row()
    {
        SecretBox box = new SecretBox(Encryption, "secrets");

        byte[] sealedSecret = box.Seal("sk-test", Row);

        Assert.Equal("sk-test", box.Open(sealedSecret, Row));
    }

    // Modules share the deployment's key, yet none opens another's secrets, nor one moved to another row.
    [Fact]
    public void A_box_never_opens_another_purposes_secret_or_one_moved_to_another_row()
    {
        SecretBox secrets = new SecretBox(Encryption, "secrets");
        SecretBox sources = new SecretBox(Encryption, "sources");

        byte[] sealedSecret = secrets.Seal("sk-test", Row);

        Assert.ThrowsAny<CryptographicException>(() => sources.Open(sealedSecret, Row));
        Assert.ThrowsAny<CryptographicException>(() => secrets.Open(sealedSecret, OtherRow));
    }

    [Fact]
    public void The_key_is_at_least_32_characters()
    {
        Assert.Throws<ArgumentException>(() => new EncryptionSettings("too short"));
    }
}
