using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MaintainXpert.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ClientId = "maintainxpert-integration";
    public const string ClientSecret = "api-tests-fixture-secret";

    static ApiFactory()
    {
        // Program.cs reads Jwt:Key/IntegrationClient synchronously while building the
        // host, before WebApplicationFactory's ConfigureWebHost overrides are merged in,
        // and it would otherwise pick up whatever local dev user-secrets happen to be set
        // on the machine running the tests. Environment variables are read earlier than
        // user secrets in the default configuration order, so setting them here (before
        // the host is first built) makes the test fixture deterministic regardless of the
        // developer's local secrets.
        var clientSecretHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ClientSecret)));

        Environment.SetEnvironmentVariable("Jwt__Key", "api-tests-fixture-signing-key-at-least-32-bytes-long");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        Environment.SetEnvironmentVariable("IntegrationClient__ClientId", ClientId);
        Environment.SetEnvironmentVariable("IntegrationClient__ClientSecretHash", clientSecretHash);
        Environment.SetEnvironmentVariable("ConnectionStrings__AzureSql", string.Empty);
    }
}
