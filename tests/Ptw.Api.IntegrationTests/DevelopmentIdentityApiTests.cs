using System.Net.Http.Json;
using Ptw.Contracts;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class DevelopmentIdentityApiTests(PtwApiFactory factory)
{
    [Fact]
    public async Task DefaultDemoIdentityHasSponsorAndAdministratorRoles()
    {
        using var client = factory.CreateClient();

        using var identityResponse = await client.GetAsync("/api/v1/me");
        identityResponse.EnsureSuccessStatusCode();
        var identity = await identityResponse.Content.ReadFromJsonAsync<MeResponse>();

        Assert.NotNull(identity);
        Assert.Equal("sponsor.demo", identity.UserId);
        Assert.Contains("Sponsor", identity.Roles);
        Assert.Contains("Administrator", identity.Roles);

        using var locations = await client.GetAsync("/api/v1/admin/locations");
        using var authorizations = await client.GetAsync("/api/v1/admin/authorizations");
        locations.EnsureSuccessStatusCode();
        authorizations.EnsureSuccessStatusCode();
    }
}
