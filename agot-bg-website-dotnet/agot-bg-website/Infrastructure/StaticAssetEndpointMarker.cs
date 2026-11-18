namespace agot_bg_website.Infrastructure;

internal sealed class StaticAssetEndpointMarker
{
    public static readonly StaticAssetEndpointMarker Instance = new();

    private StaticAssetEndpointMarker() { }

    public static bool IsStaticAsset(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<StaticAssetEndpointMarker>() is not null;
}
