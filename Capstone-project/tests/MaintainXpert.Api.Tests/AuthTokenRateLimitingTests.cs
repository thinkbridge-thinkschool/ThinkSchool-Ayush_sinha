using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace MaintainXpert.Api.Tests;

// Regression coverage for the Day 31 security fix: Day 27's threat model flagged /auth/token as
// unbounded against credential-stuffing and left it as an accepted risk. Runs through the real
// application pipeline (DI, middleware, routing) via WebApplicationFactory, using its own ApiFactory
// instance so its rate-limiter state never interacts with other test classes' token calls.
public class AuthTokenRateLimitingTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthTokenRateLimitingTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Auth_token_serves_valid_requests_within_the_limit_then_rejects_once_it_is_exceeded()
    {
        var client = _factory.CreateClient();

        // First call, well within the fixed window's limit: the limiter must not interfere
        // with a legitimate caller.
        var firstResponse = await client.PostAsJsonAsync("/auth/token", new
        {
            ClientId = ApiFactory.ClientId,
            ClientSecret = ApiFactory.ClientSecret
        });
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Same bucket, 14 more attempts with a wrong secret. The fixed window in Program.cs
        // permits 10 requests/minute, so this run of 15 total requests against one bucket must
        // cross into 429 before it ends.
        var statusCodes = new List<HttpStatusCode>();

        for (var i = 0; i < 14; i++)
        {
            var response = await client.PostAsJsonAsync("/auth/token", new
            {
                ClientId = ApiFactory.ClientId,
                ClientSecret = "not-the-real-secret"
            });

            statusCodes.Add(response.StatusCode);
        }

        statusCodes.Should().Contain(HttpStatusCode.TooManyRequests);
        statusCodes.Should().Contain(HttpStatusCode.Unauthorized);
    }
}
