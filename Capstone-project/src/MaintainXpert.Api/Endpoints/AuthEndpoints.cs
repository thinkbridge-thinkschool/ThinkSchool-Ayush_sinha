using System.ComponentModel.DataAnnotations;
using MaintainXpert.Api.Infrastructure;

namespace MaintainXpert.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/token", (TokenRequest request, TokenService tokenService, ILogger<TokenRequest> logger) =>
        {
            if (!tokenService.ValidateClientCredentials(request.ClientId, request.ClientSecret))
            {
                logger.LogWarning("Token request rejected for client {ClientId}", request.ClientId);

                return Results.Unauthorized();
            }

            var (accessToken, expiresIn) = tokenService.CreateAccessToken(request.ClientId);

            logger.LogInformation("Token issued for client {ClientId}", request.ClientId);

            return Results.Ok(new TokenResponse(accessToken, "Bearer", expiresIn));
        })
        .RequireRateLimiting(RateLimiterPolicies.AuthToken);

        return group;
    }
}

public sealed record TokenRequest(
    [property: Required] string ClientId,
    [property: Required] string ClientSecret);

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);
