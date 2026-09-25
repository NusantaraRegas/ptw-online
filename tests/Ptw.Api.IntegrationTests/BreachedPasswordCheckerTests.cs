using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ptw.Application;
using Ptw.Infrastructure.Security;

namespace Ptw.Api.IntegrationTests;

public sealed class BreachedPasswordCheckerTests
{
    // SHA-1("Password2026!") is irrelevant here: the deny-list refuses it before any network call.
    [Theory]
    [InlineData("Password2026!")]
    [InlineData("P@ssw0rd12345")]
    [InlineData("Administrator99")]
    [InlineData("NusantaraRegas2026")]
    [InlineData("!!Welcome-2026!!")]
    public async Task OfflineDenyListRefusesPaddedRootWords(string password)
    {
        var checker = new BreachedPasswordChecker(
            new BreachedPasswordSettings { OnlineEnabled = false },
            new HttpClient(new ThrowingHandler()),
            NullLogger<BreachedPasswordChecker>.Instance);

        Assert.True(await checker.IsBreachedAsync(password, CancellationToken.None));
    }

    [Fact]
    public async Task OfflineModeNeverCallsTheNetwork()
    {
        var checker = new BreachedPasswordChecker(
            new BreachedPasswordSettings { OnlineEnabled = false },
            new HttpClient(new ThrowingHandler()),
            NullLogger<BreachedPasswordChecker>.Instance);

        Assert.False(await checker.IsBreachedAsync("Correct-Horse-Battery-42", CancellationToken.None));
    }

    [Fact]
    public async Task OnlineCheckSendsOnlyTheHashPrefixAndMatchesTheSuffixLocally()
    {
        // SHA-1("Correct-Horse-Battery-42") computed by the checker; the fake answers with its suffix.
        const string password = "Correct-Horse-Battery-42";
#pragma warning disable CA5350
        var sha1 = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
        var handler = new RecordingHandler($"00000AAAA:0\n{sha1[5..]}:12\nFFFFFBBBB:3\n");
        var checker = new BreachedPasswordChecker(
            new BreachedPasswordSettings { OnlineEnabled = true, BaseUrl = new Uri("https://range.example.test/range/") },
            new HttpClient(handler),
            NullLogger<BreachedPasswordChecker>.Instance);

        Assert.True(await checker.IsBreachedAsync(password, CancellationToken.None));
        Assert.Equal($"https://range.example.test/range/{sha1[..5]}", handler.RequestedUri?.AbsoluteUri);
        Assert.DoesNotContain(password, handler.RequestedUri?.AbsoluteUri ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("true", handler.AddPaddingHeader);

        handler.Body = "00000AAAA:0\n";
        Assert.False(await checker.IsBreachedAsync(password, CancellationToken.None));
    }

    [Fact]
    public async Task OnlineOutageRefusesThePasswordInsteadOfPassingIt()
    {
        var checker = new BreachedPasswordChecker(
            new BreachedPasswordSettings { OnlineEnabled = true, BaseUrl = new Uri("https://range.example.test/range/") },
            new HttpClient(new ThrowingHandler()),
            NullLogger<BreachedPasswordChecker>.Instance);

        await Assert.ThrowsAsync<BreachedPasswordCheckUnavailableException>(() =>
            checker.IsBreachedAsync("Correct-Horse-Battery-42", CancellationToken.None));
    }

    [Fact]
    public void OnlineCheckRequiresHttps()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PasswordPolicy:BreachCheck:Enabled"] = "true",
            ["PasswordPolicy:BreachCheck:BaseUrl"] = "http://range.example.test/"
        }).Build();

        Assert.Throws<InvalidOperationException>(() => BreachedPasswordSettings.FromConfiguration(configuration));
        Assert.False(BreachedPasswordSettings.FromConfiguration(new ConfigurationBuilder().Build()).OnlineEnabled);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("network unavailable");
    }

    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public string Body { get; set; } = body;
        public Uri? RequestedUri { get; private set; }
        public string? AddPaddingHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUri = request.RequestUri;
            AddPaddingHeader = request.Headers.TryGetValues("Add-Padding", out var values) ? string.Join(",", values) : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
        }
    }
}
