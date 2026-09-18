using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;

namespace MaintainXpert.E2E.Tests;

// Genuine end-to-end test: launches the real MaintainXpert.Api process on a real Kestrel socket
// and drives the full asset + work-order business flow purely over HTTP against that separate
// process, not the in-memory WebApplicationFactory transport the integration tests in
// MaintainXpert.Api.Tests use. This is the one externally observable entry point the exercise
// calls for - deliberately a different transport from the WebApplicationFactory happy path, not
// a copy of it.
public sealed class WorkOrderLifecycleEndToEndTests : IAsyncLifetime
{
    private const string ClientId = "maintainxpert-e2e";
    private const string ClientSecret = "e2e-fixture-secret";

    private readonly HttpClient _client = new();
    private readonly StringBuilder _processOutput = new();
    private Process? _apiProcess;

    public async Task InitializeAsync()
    {
        var port = GetFreeTcpPort();
        var baseUrl = $"http://127.0.0.1:{port}";
        _client.BaseAddress = new Uri(baseUrl);

        var apiDllPath = typeof(Program).Assembly.Location;
        var clientSecretHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ClientSecret)));

        var startInfo = new ProcessStartInfo("dotnet", $"\"{apiDllPath}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.Environment["ASPNETCORE_URLS"] = baseUrl;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["Jwt__Key"] = "e2e-fixture-signing-key-at-least-32-bytes-long";
        startInfo.Environment["Jwt__AccessTokenMinutes"] = "15";
        startInfo.Environment["IntegrationClient__ClientId"] = ClientId;
        startInfo.Environment["IntegrationClient__ClientSecretHash"] = clientSecretHash;
        startInfo.Environment["ConnectionStrings__AzureSql"] = string.Empty;

        _apiProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the API process for the E2E test.");

        _apiProcess.OutputDataReceived += (_, e) => { if (e.Data is not null) _processOutput.AppendLine(e.Data); };
        _apiProcess.ErrorDataReceived += (_, e) => { if (e.Data is not null) _processOutput.AppendLine(e.Data); };
        _apiProcess.BeginOutputReadLine();
        _apiProcess.BeginErrorReadLine();

        await WaitUntilHealthyAsync(TimeSpan.FromSeconds(30));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();

        if (_apiProcess is { HasExited: false })
        {
            _apiProcess.Kill(entireProcessTree: true);
            await _apiProcess.WaitForExitAsync();
        }

        _apiProcess?.Dispose();
    }

    [Fact]
    public async Task Full_asset_and_work_order_lifecycle_succeeds_against_a_real_running_process()
    {
        var tokenResponse = await _client.PostAsJsonAsync("/auth/token", new { ClientId, ClientSecret });
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK, BecauseOfProcessOutput());
        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponseDto>();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var assetResponse = await _client.PostAsJsonAsync("/api/v1/assets", new { Name = "E2E Compressor" });
        assetResponse.StatusCode.Should().Be(HttpStatusCode.Created, BecauseOfProcessOutput());
        var asset = await assetResponse.Content.ReadFromJsonAsync<AssetDto>();

        var workOrderResponse = await _client.PostAsJsonAsync("/api/v1/work-orders", new
        {
            AssetId = asset!.Id,
            Description = "Replace worn belt (E2E)",
            Priority = "High"
        });
        workOrderResponse.StatusCode.Should().Be(HttpStatusCode.Created, BecauseOfProcessOutput());
        var workOrder = await workOrderResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        var assignResponse = await _client.PostAsJsonAsync(
            $"/api/v1/work-orders/{workOrder!.Id}/assign",
            new { TechnicianId = Guid.NewGuid() });
        assignResponse.StatusCode.Should().Be(HttpStatusCode.OK, BecauseOfProcessOutput());

        var startResponse = await _client.PostAsync($"/api/v1/work-orders/{workOrder.Id}/start", content: null);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK, BecauseOfProcessOutput());

        var duringMaintenance = await _client.GetAsync($"/api/v1/assets/{asset.Id}");
        (await duringMaintenance.Content.ReadFromJsonAsync<AssetDto>())!.Status.Should().Be("UnderMaintenance");

        var completeResponse = await _client.PostAsync($"/api/v1/work-orders/{workOrder.Id}/complete", content: null);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK, BecauseOfProcessOutput());

        var finalAssetResponse = await _client.GetAsync($"/api/v1/assets/{asset.Id}");
        var finalAsset = await finalAssetResponse.Content.ReadFromJsonAsync<AssetDto>();
        finalAsset!.Status.Should().Be("Operational");
        finalAsset.LastMaintenanceCompletedAt.Should().NotBeNull();
    }

    private string BecauseOfProcessOutput() => $"the API process logged:\n{_processOutput}";

    private async Task WaitUntilHealthyAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        while (!cts.IsCancellationRequested)
        {
            if (_apiProcess!.HasExited)
            {
                throw new InvalidOperationException($"The API process exited early:\n{_processOutput}");
            }

            try
            {
                var response = await _client.GetAsync("/health", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception) when (!cts.IsCancellationRequested)
            {
                // The process hasn't finished binding its listener yet; retry until the timeout.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), CancellationToken.None);
        }

        throw new TimeoutException($"The API process did not become healthy within {timeout}:\n{_processOutput}");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed record TokenResponseDto(string AccessToken, string TokenType, int ExpiresIn);

    private sealed record AssetDto(Guid Id, string Name, string Status, DateTimeOffset? LastMaintenanceCompletedAt);

    private sealed record WorkOrderDto(Guid Id);
}
