using System.Net;
using System.Net.Http.Json;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class RemovedPolicyModulesApiTests(PtwApiFactory factory)
{
    [Theory]
    [InlineData("/api/v1/admin/policy-readiness")]
    [InlineData("/api/v1/admin/policy-uat-suites")]
    public async Task RemovedPolicyModulesAreNotExposed(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemovedPolicySimulationIsNotExposed()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/policy-simulations",
            new { SubjectId = "operator", ActionCode = "permit.create", LocationCode = "ORF" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
