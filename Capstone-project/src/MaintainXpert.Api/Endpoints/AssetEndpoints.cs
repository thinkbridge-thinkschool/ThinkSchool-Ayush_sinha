using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Asp.Versioning.Builder;
using MaintainXpert.Api.Infrastructure;
using MaintainXpert.Assets.Application;
using MaintainXpert.Assets.Domain;
using MaintainXpert.SharedKernel;

namespace MaintainXpert.Api.Endpoints;

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this WebApplication app)
    {
        var versionSet = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .ReportApiVersions()
            .Build();

        var group = app.MapGroup("/api/v{version:apiVersion}/assets")
            .WithApiVersionSet(versionSet);

        group.MapPost("/", async (RegisterAssetRequest request, IAssetRepository repository) =>
        {
            var validationProblem = ValidationExtensions.Validate(request);

            if (validationProblem is not null)
            {
                return validationProblem;
            }

            var asset = Asset.Register(request.Name);
            await repository.AddAsync(asset);
            return Results.Created($"/api/v1/assets/{asset.Id}", ToResponse(asset));
        })
        .RequireAuthorization("workorders.write");

        group.MapGet("/{id:guid}", async (Guid id, IAssetRepository repository) =>
        {
            var asset = await repository.GetByIdAsync(new AssetId(id));
            return asset is null ? Results.NotFound() : Results.Ok(ToResponse(asset));
        });

        group.MapPost("/{id:guid}/decommission", async (Guid id, IAssetRepository repository) =>
        {
            var asset = await repository.GetByIdAsync(new AssetId(id));

            if (asset is null)
            {
                return Results.NotFound();
            }

            asset.Decommission();
            await repository.UpdateAsync(asset);
            return Results.Ok(ToResponse(asset));
        })
        .RequireAuthorization("workorders.write");
    }

    private static AssetResponse ToResponse(Asset asset) => new(
        asset.Id.Value,
        asset.Name,
        asset.Status.ToString(),
        asset.LastMaintenanceCompletedAt);
}

public sealed record RegisterAssetRequest(
    [property: Required, StringLength(200, MinimumLength = 1)] string Name);

public sealed record AssetResponse(Guid Id, string Name, string Status, DateTimeOffset? LastMaintenanceCompletedAt);
