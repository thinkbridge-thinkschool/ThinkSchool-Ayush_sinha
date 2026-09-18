using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace MaintainXpert.Api.Tests;

public class WorkOrderHappyPathTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public WorkOrderHappyPathTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();

        var tokenResponse = await client.PostAsJsonAsync("/auth/token", new
        {
            ClientId = ApiFactory.ClientId,
            ClientSecret = ApiFactory.ClientSecret
        });

        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        return client;
    }

    [Fact]
    public async Task Full_lifecycle_creates_asset_raises_work_order_and_updates_asset_on_completion()
    {
        var client = await CreateAuthenticatedClientAsync();

        var assetResponse = await client.PostAsJsonAsync("/api/v1/assets", new { Name = "HVAC Unit 4" });
        assetResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var asset = await assetResponse.Content.ReadFromJsonAsync<AssetDto>();

        var workOrderResponse = await client.PostAsJsonAsync("/api/v1/work-orders", new
        {
            AssetId = asset!.Id,
            Description = "Replace worn belt",
            Priority = "Medium"
        });
        workOrderResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var workOrder = await workOrderResponse.Content.ReadFromJsonAsync<WorkOrderDto>();
        workOrder!.Status.Should().Be("Open");

        var assignResponse = await client.PostAsJsonAsync(
            $"/api/v1/work-orders/{workOrder.Id}/assign",
            new { TechnicianId = Guid.NewGuid() });
        assignResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var startResponse = await client.PostAsync($"/api/v1/work-orders/{workOrder.Id}/start", content: null);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await startResponse.Content.ReadFromJsonAsync<WorkOrderDto>())!.Status.Should().Be("InProgress");

        var duringMaintenanceResponse = await client.GetAsync($"/api/v1/assets/{asset.Id}");
        (await duringMaintenanceResponse.Content.ReadFromJsonAsync<AssetDto>())!.Status.Should().Be("UnderMaintenance");

        var completeResponse = await client.PostAsync($"/api/v1/work-orders/{workOrder.Id}/complete", content: null);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await completeResponse.Content.ReadFromJsonAsync<WorkOrderDto>())!.Status.Should().Be("Completed");

        var finalAssetResponse = await client.GetAsync($"/api/v1/assets/{asset.Id}");
        finalAssetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalAsset = await finalAssetResponse.Content.ReadFromJsonAsync<AssetDto>();
        finalAsset!.LastMaintenanceCompletedAt.Should().NotBeNull();
        finalAsset.Status.Should().Be("Operational");
    }

    [Fact]
    public async Task Creating_a_work_order_for_a_nonexistent_asset_returns_not_found()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/work-orders", new
        {
            AssetId = Guid.NewGuid(),
            Description = "Replace worn belt",
            Priority = "Medium"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Creating_a_work_order_for_a_decommissioned_asset_is_rejected()
    {
        var client = await CreateAuthenticatedClientAsync();

        var assetResponse = await client.PostAsJsonAsync("/api/v1/assets", new { Name = "Retired Compressor" });
        var asset = await assetResponse.Content.ReadFromJsonAsync<AssetDto>();

        var decommissionResponse = await client.PostAsync($"/api/v1/assets/{asset!.Id}/decommission", content: null);
        decommissionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await decommissionResponse.Content.ReadFromJsonAsync<AssetDto>())!.Status.Should().Be("Decommissioned");

        var response = await client.PostAsJsonAsync("/api/v1/work-orders", new
        {
            AssetId = asset.Id,
            Description = "Replace worn belt",
            Priority = "Medium"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Decommissioning_a_nonexistent_asset_returns_not_found()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsync($"/api/v1/assets/{Guid.NewGuid()}/decommission", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Decommissioning_an_already_decommissioned_asset_returns_conflict()
    {
        var client = await CreateAuthenticatedClientAsync();

        var assetResponse = await client.PostAsJsonAsync("/api/v1/assets", new { Name = "Retired Pump" });
        var asset = await assetResponse.Content.ReadFromJsonAsync<AssetDto>();
        await client.PostAsync($"/api/v1/assets/{asset!.Id}/decommission", content: null);

        var response = await client.PostAsync($"/api/v1/assets/{asset.Id}/decommission", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Creating_a_work_order_without_a_description_returns_a_validation_problem()
    {
        var client = await CreateAuthenticatedClientAsync();

        var assetResponse = await client.PostAsJsonAsync("/api/v1/assets", new { Name = "Conveyor Belt 2" });
        var asset = await assetResponse.Content.ReadFromJsonAsync<AssetDto>();

        var response = await client.PostAsJsonAsync("/api/v1/work-orders", new
        {
            AssetId = asset!.Id,
            Description = "",
            Priority = "Medium"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Completing_a_nonexistent_work_order_returns_not_found()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsync($"/api/v1/work-orders/{Guid.NewGuid()}/complete", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Registering_an_asset_without_a_token_is_rejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/assets", new { Name = "Unauthorized Asset" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record TokenResponseDto(string AccessToken, string TokenType, int ExpiresIn);

    private sealed record AssetDto(Guid Id, string Name, string Status, DateTimeOffset? LastMaintenanceCompletedAt);

    private sealed record WorkOrderDto(
        Guid Id,
        Guid AssetId,
        string Description,
        string Priority,
        string Status,
        Guid? AssignedTechnicianId,
        DateTimeOffset CreatedAt);
}
