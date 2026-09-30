using OptiGame.Platform.Artwork;

namespace OptiGame.Platform.Tests;

public sealed class SecretProtectorTests
{
    [Fact]
    public void Secret_round_trips_and_is_not_stored_in_clear()
    {
        const string secret = "hijklmn67890";

        var protectedValue = SecretProtector.Protect(secret);

        Assert.DoesNotContain(secret, protectedValue);
        Assert.Equal(secret, SecretProtector.Unprotect(protectedValue));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pas du base64 !")]
    [InlineData("AAAA")]
    public void Unreadable_values_give_null(string? value)
    {
        Assert.Null(SecretProtector.Unprotect(value));
    }
}
