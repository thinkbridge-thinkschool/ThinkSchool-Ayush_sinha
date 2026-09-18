using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

// Hot-path benchmark for Day 31: POST /api/v1/work-orders is the busiest write endpoint in
// MaintainXpert (validation + asset lookup + repository write + domain-event dispatch on every
// call), and every write endpoint shares the reflection-based ValidationExtensions.Validate<T>
// this session's perf fix targets. Launches the real API as a separate process (matching the
// E2E test's methodology) and measures real HTTP round-trip latency against it, not an
// in-process shortcut.

const string ClientId = "maintainxpert-benchmark";
const string ClientSecret = "benchmark-fixture-secret";
const int WarmupRequests = 20;
const int MeasuredRequests = 300;

var apiProjectPath = GetApiProjectPath();

if (!File.Exists(apiProjectPath))
{
    Console.Error.WriteLine($"Could not find the API project at {apiProjectPath}.");
    return 1;
}

var port = GetFreeTcpPort();
var baseUrl = $"http://127.0.0.1:{port}";
var clientSecretHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ClientSecret)));

var startInfo = new ProcessStartInfo("dotnet", $"run --project \"{apiProjectPath}\" --no-launch-profile --configuration Release")
{
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true,
    WorkingDirectory = Path.GetDirectoryName(apiProjectPath),
};

startInfo.Environment["ASPNETCORE_URLS"] = baseUrl;
startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
startInfo.Environment["Jwt__Key"] = "benchmark-fixture-signing-key-at-least-32-bytes-long";
startInfo.Environment["Jwt__AccessTokenMinutes"] = "15";
startInfo.Environment["IntegrationClient__ClientId"] = ClientId;
startInfo.Environment["IntegrationClient__ClientSecretHash"] = clientSecretHash;
startInfo.Environment["ConnectionStrings__AzureSql"] = string.Empty;

var processOutput = new StringBuilder();

using var process = Process.Start(startInfo)
    ?? throw new InvalidOperationException("Failed to start MaintainXpert.Api for the benchmark.");

process.OutputDataReceived += (_, e) => { if (e.Data is not null) processOutput.AppendLine(e.Data); };
process.ErrorDataReceived += (_, e) => { if (e.Data is not null) processOutput.AppendLine(e.Data); };
process.BeginOutputReadLine();
process.BeginErrorReadLine();

using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };

try
{
    Console.WriteLine($"Starting MaintainXpert.Api ({apiProjectPath}) on {baseUrl}...");
    await WaitUntilHealthyAsync(client, process, processOutput, TimeSpan.FromSeconds(60));

    var tokenResponse = await client.PostAsJsonAsync("/auth/token", new { ClientId, ClientSecret });
    tokenResponse.EnsureSuccessStatusCode();
    var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponseDto>();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

    var assetResponse = await client.PostAsJsonAsync("/api/v1/assets", new { Name = "Benchmark Asset" });
    assetResponse.EnsureSuccessStatusCode();
    var asset = await assetResponse.Content.ReadFromJsonAsync<AssetDto>();

    Console.WriteLine($"Warming up ({WarmupRequests} requests)...");
    for (var i = 0; i < WarmupRequests; i++)
    {
        await CreateWorkOrderAsync(client, asset!.Id);
    }

    Console.WriteLine($"Measuring ({MeasuredRequests} requests) against POST /api/v1/work-orders...");
    var latenciesMs = new List<double>(MeasuredRequests);

    for (var i = 0; i < MeasuredRequests; i++)
    {
        var stopwatch = Stopwatch.StartNew();
        await CreateWorkOrderAsync(client, asset!.Id);
        stopwatch.Stop();
        latenciesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
    }

    latenciesMs.Sort();

    Console.WriteLine();
    Console.WriteLine("=== Hot-path benchmark: POST /api/v1/work-orders ===");
    Console.WriteLine($"Requests measured: {MeasuredRequests} (after {WarmupRequests} warm-up requests)");
    Console.WriteLine($"min:  {latenciesMs[0]:F3} ms");
    Console.WriteLine($"p50:  {Percentile(latenciesMs, 50):F3} ms");
    Console.WriteLine($"p95:  {Percentile(latenciesMs, 95):F3} ms");
    Console.WriteLine($"p99:  {Percentile(latenciesMs, 99):F3} ms");
    Console.WriteLine($"max:  {latenciesMs[^1]:F3} ms");
    Console.WriteLine($"mean: {latenciesMs.Average():F3} ms");
}
finally
{
    if (!process.HasExited)
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit(5000);
    }
}

return 0;

static async Task CreateWorkOrderAsync(HttpClient client, Guid assetId)
{
    var response = await client.PostAsJsonAsync("/api/v1/work-orders", new
    {
        AssetId = assetId,
        Description = "Routine inspection",
        Priority = "Low"
    });
    response.EnsureSuccessStatusCode();
}

static double Percentile(List<double> sortedValues, double percentile)
{
    if (sortedValues.Count == 1)
    {
        return sortedValues[0];
    }

    var rank = (percentile / 100.0) * (sortedValues.Count - 1);
    var lowerIndex = (int)Math.Floor(rank);
    var upperIndex = (int)Math.Ceiling(rank);

    if (lowerIndex == upperIndex)
    {
        return sortedValues[lowerIndex];
    }

    var fraction = rank - lowerIndex;
    return sortedValues[lowerIndex] + (sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction;
}

static async Task WaitUntilHealthyAsync(HttpClient client, Process process, StringBuilder processOutput, TimeSpan timeout)
{
    using var cts = new CancellationTokenSource(timeout);

    while (!cts.IsCancellationRequested)
    {
        if (process.HasExited)
        {
            throw new InvalidOperationException($"The API process exited early:\n{processOutput}");
        }

        try
        {
            var response = await client.GetAsync("/health", cts.Token);
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch (Exception) when (!cts.IsCancellationRequested)
        {
            // Still starting up (build + bind); retry until the timeout.
        }

        await Task.Delay(TimeSpan.FromMilliseconds(250));
    }

    throw new TimeoutException($"The API process did not become healthy within {timeout}:\n{processOutput}");
}

static int GetFreeTcpPort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var freePort = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return freePort;
}

static string GetApiProjectPath([CallerFilePath] string sourceFilePath = "")
{
    var directory = Path.GetDirectoryName(sourceFilePath)!;
    return Path.GetFullPath(Path.Combine(directory, "..", "..", "..", "src", "MaintainXpert.Api", "MaintainXpert.Api.csproj"));
}

sealed record TokenResponseDto(string AccessToken, string TokenType, int ExpiresIn);

sealed record AssetDto(Guid Id);
