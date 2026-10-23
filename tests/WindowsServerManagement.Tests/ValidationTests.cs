using WindowsServerManagement.Core.Models;
using WindowsServerManagement.Core.Services;
using WindowsServerManagement.Core.Services.Validation;

namespace WindowsServerManagement.Tests;

public sealed class ValidationTests
{
    [Fact]
    public void ValidPasswordProfile_Passes()
    {
        var profile = new ServerProfile
        {
            Name = "Web 01",
            Host = "192.0.2.10",
            Port = 22,
            Username = "administrator",
            AuthenticationMethod = AuthenticationMethod.Password
        };

        var result = ServerProfileValidator.Validate(profile, hasStoredSecret: false, newSecret: "secret");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void PasswordProfileWithoutSecret_Fails()
    {
        var profile = new ServerProfile
        {
            Name = "Web 01",
            Host = "web01.example.com",
            Port = 22,
            Username = "administrator",
            AuthenticationMethod = AuthenticationMethod.Password
        };

        var result = ServerProfileValidator.Validate(profile, hasStoredSecret: false, newSecret: null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrivateKeyWithoutPassphrase_IsAllowed()
    {
        var keyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".key");
        File.WriteAllText(keyPath, "test");
        try
        {
            var profile = new ServerProfile
            {
                Name = "Web 01",
                Host = "web01.example.com",
                Port = 22,
                Username = "administrator",
                AuthenticationMethod = AuthenticationMethod.PrivateKey,
                PrivateKeyPath = keyPath
            };

            var result = ServerProfileValidator.Validate(profile, hasStoredSecret: false, newSecret: null);

            Assert.True(result.IsValid);
        }
        finally
        {
            File.Delete(keyPath);
        }
    }

    [Fact]
    public void IPv6Host_IsAccepted()
    {
        var profile = new ServerProfile
        {
            Name = "IPv6 server",
            Host = "::1",
            Port = 22,
            Username = "admin",
            AuthenticationMethod = AuthenticationMethod.Password
        };

        var result = ServerProfileValidator.Validate(profile, hasStoredSecret: true, newSecret: "secret");

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad host")]
    [InlineData("http://server")]
    [InlineData("host:22")]
    public void InvalidHosts_AreRejected(string host)
    {
        var profile = new ServerProfile
        {
            Name = "Server",
            Host = host,
            Port = 22,
            Username = "user",
            AuthenticationMethod = AuthenticationMethod.Password
        };

        var result = ServerProfileValidator.Validate(profile, hasStoredSecret: true, newSecret: "secret");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void WebsiteValidation_AcceptsExpectedExample()
    {
        var request = new WebsiteCreateRequest
        {
            Name = "MyPortfolio",
            FolderName = "MyPortfolio",
            Port = 50082,
            ApplicationPoolName = "MyPortfolio",
            IisRoot = @"C:\inetpub"
        };

        var result = WebsiteValidator.ValidateCreateRequest(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("folder\\child")]
    [InlineData("CON")]
    [InlineData("folder.")]
    public void WebsiteValidation_RejectsUnsafeFolderNames(string folder)
    {
        var request = new WebsiteCreateRequest
        {
            Name = "Site",
            FolderName = folder,
            Port = 8080,
            ApplicationPoolName = "Site",
            IisRoot = @"C:\inetpub"
        };

        var result = WebsiteValidator.ValidateCreateRequest(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void HttpsValidation_RequiresThumbprint()
    {
        var request = new WebsiteCreateRequest
        {
            Name = "Site",
            FolderName = "Site",
            Port = 443,
            ApplicationPoolName = "Site",
            IisRoot = @"C:\inetpub",
            UseHttps = true
        };

        var result = WebsiteValidator.ValidateCreateRequest(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("certificate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HttpsValidation_AcceptsValidThumbprint()
    {
        var request = new WebsiteCreateRequest
        {
            Name = "Site",
            FolderName = "Site",
            Port = 443,
            ApplicationPoolName = "Site",
            IisRoot = @"C:\inetpub",
            UseHttps = true,
            CertificateThumbprint = "a909502dd82ae41433e6f83886b00d4277a32a7b"
        };

        var result = WebsiteValidator.ValidateCreateRequest(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void BindingBuilder_CreatesWildcardBinding()
    {
        Assert.Equal("*:50082:", IisBindingBuilder.BuildBindingInformation(null, 50082));
        Assert.Equal("http://*:50082/", IisBindingBuilder.BuildDisplay("http", 50082, null));
        Assert.Equal("https://portfolio.example.com:443/", IisBindingBuilder.BuildDisplay("https", 443, "portfolio.example.com"));
    }

    [Fact]
    public void BindingParser_RoundTripsIPv4AndIPv6()
    {
        Assert.True(IisBindingBuilder.TryParseBindingInformation("*:8080:example.com", out var ip, out var port, out var host));
        Assert.Equal("*", ip);
        Assert.Equal(8080, port);
        Assert.Equal("example.com", host);

        Assert.True(IisBindingBuilder.TryParseBindingInformation("[::]:443:secure.example.com", out var ipv6, out var port2, out var host2));
        Assert.Equal("[::]", ipv6);
        Assert.Equal(443, port2);
        Assert.Equal("secure.example.com", host2);
    }

    [Fact]
    public void PathSafety_BuildsPathInsideRoot()
    {
        var ok = WebsitePathSafety.BuildPhysicalPath(@"C:\inetpub", "MyPortfolio", out var path, out var error);

        Assert.True(ok, error);
        Assert.Equal(@"C:\inetpub\MyPortfolio", path);
        Assert.True(WebsitePathSafety.IsUnderRoot(path, @"C:\inetpub"));
        Assert.False(WebsitePathSafety.IsUnderRoot(@"C:\Windows\System32", @"C:\inetpub"));
    }

    [Fact]
    public void BindingValidation_RejectsDuplicates()
    {
        var bindings = new List<WebsiteBinding>
        {
            IisBindingBuilder.Create("http", 8080, "example.com"),
            IisBindingBuilder.Create("http", 8080, "example.com")
        };

        var result = WebsiteValidator.ValidateBindings(bindings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }
}
