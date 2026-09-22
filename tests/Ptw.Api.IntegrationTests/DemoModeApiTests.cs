using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ptw.Contracts;
using Ptw.Infrastructure.Persistence;

namespace Ptw.Api.IntegrationTests;

[Collection(PtwApiTestGroup.Name)]
public sealed class DemoModeApiTests(PtwApiFactory factory)
{
    [Fact]
    public async Task AdministratorCanDisableDemoModeAndServerRejectsDemoIdentity()
    {
        try
        {
            using var publicClient = factory.CreateClient();
            var publicSetting = await publicClient.GetFromJsonAsync<AuthenticationOptionsResponse>("/api/v1/auth/options");
            Assert.NotNull(publicSetting);
            Assert.True(publicSetting.DemoModeEnabled);

            using var sponsor = DemoClient("demo-mode.sponsor", "Sponsor");
            using var denied = await sponsor.GetAsync("/api/v1/admin/settings/demo-mode");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            using var admin = DemoClient("demo-mode.admin", "Administrator");
            var setting = await admin.GetFromJsonAsync<DemoModeResponse>("/api/v1/admin/settings/demo-mode");
            Assert.NotNull(setting);

            using var stale = Command("disable", "\"999\"");
            using var staleResponse = await admin.SendAsync(stale);
            Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

            using var disable = Command("disable", setting.ETag);
            using var disabledResponse = await admin.SendAsync(disable);
            disabledResponse.EnsureSuccessStatusCode();
            var disabled = await disabledResponse.Content.ReadFromJsonAsync<DemoModeResponse>();
            Assert.NotNull(disabled);
            Assert.False(disabled.Enabled);

            var publicDisabled = await publicClient.GetFromJsonAsync<AuthenticationOptionsResponse>("/api/v1/auth/options");
            Assert.NotNull(publicDisabled);
            Assert.False(publicDisabled.DemoModeEnabled);

            using var rejectedDemo = DemoClient("demo-mode.rejected", "Administrator");
            using var protectedResponse = await rejectedDemo.GetAsync("/api/v1/admin/locations");
            Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
        }
        finally
        {
            await RestoreDemoModeAsync();
        }
    }

    private HttpClient DemoClient(string userId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Name", userId);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", role);
        return client;
    }

    private static HttpRequestMessage Command(string command, string eTag)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/settings/demo-mode/{command}")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.TryAddWithoutValidation("If-Match", eTag);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return request;
    }

    private async Task RestoreDemoModeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PtwDbContext>();
        var record = await dbContext.DemoModeSettings.SingleOrDefaultAsync();
        if (record is null || record.Enabled)
        {
            return;
        }

        record.Enabled = true;
        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        record.UpdatedBy = "integration-test-cleanup";
        await dbContext.SaveChangesAsync();
    }
}
