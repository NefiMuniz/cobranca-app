using Cobranca.Api.Auth;

namespace Cobranca.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_then_verify_round_trips()
    {
        var hash = PasswordHasher.Hash("correct horse");

        Assert.True(PasswordHasher.Verify("correct horse", hash));
        Assert.False(PasswordHasher.Verify("wrong horse", hash));
    }

    [Fact]
    public void Same_password_gets_a_different_salt_each_time()
    {
        Assert.NotEqual(PasswordHasher.Hash("same"), PasswordHasher.Hash("same"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain-text-password")]
    [InlineData("PBKDF2$abc$salt$hash")]
    [InlineData("PBKDF2$100$c2FsdA==$aGFzaA==")]
    [InlineData("PBKDF2$210000$not-base64$not-base64")]
    public void Malformed_hashes_never_verify(string stored)
    {
        Assert.False(PasswordHasher.Verify("anything", stored));
    }

    [Fact]
    public void Verifies_a_hash_generated_outside_dotnet()
    {
        // Fixture generated with Python's hashlib.pbkdf2_hmac: proves the stored format is interoperable.
        // It is not a credential for any environment.
        const string externalHash = "PBKDF2$210000$TsADWByAl5xtyu1Abze6DQ==$+RwXTmy/Yv2JdiJsHZOCgxDNwTHU2E9C1PZyLAzmlvs=";

        Assert.True(PasswordHasher.Verify("interop-check", externalHash));
    }
}
