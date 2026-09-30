using OptiGame.Platform.Drivers;

namespace OptiGame.Platform.Tests;

/// <summary>Signature réelle d'un fichier du runtime .NET (signé Microsoft), puis d'une copie altérée d'un octet.</summary>
public sealed class AuthenticodeVerifierTests
{
    private static string SignedFile => typeof(object).Assembly.Location; // System.Private.CoreLib.dll

    [Fact]
    public void Accepts_an_intact_signed_file_and_reads_its_signer()
    {
        var check = AuthenticodeVerifier.Verify(SignedFile);

        Assert.True(check.IsValid, check.Detail);
        Assert.Contains("O=Microsoft Corporation", check.Signer);
        Assert.False(Core.Drivers.InstallerSignature.IsNvidia(check.Signer));
    }

    [Fact]
    public void Rejects_a_file_modified_after_signing()
    {
        var copy = Path.Combine(Path.GetTempPath(), $"optigame-signature-{Guid.NewGuid():N}.dll");
        try
        {
            var bytes = File.ReadAllBytes(SignedFile);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(copy, bytes);

            var check = AuthenticodeVerifier.Verify(copy);

            Assert.False(check.IsValid);
            Assert.Null(check.Signer);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [Fact]
    public void Rejects_an_unsigned_file()
    {
        var file = Path.Combine(Path.GetTempPath(), $"optigame-unsigned-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(file, [0x4D, 0x5A, 0, 0]);
            Assert.False(AuthenticodeVerifier.Verify(file).IsValid);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
