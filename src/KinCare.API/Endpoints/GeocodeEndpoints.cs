using KinCare.API.Services;

namespace KinCare.API.Endpoints;

public static class GeocodeEndpoints
{
    public static void MapGeocodeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/geocode").WithTags("Geocode").RequireAuthorization();

        group.MapGet("/autocomplete", Autocomplete);
    }

    private static async Task<IResult> Autocomplete(string query, GeocodingService geocoding)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3)
            return Results.Ok(new List<AddressSuggestion>());

        var suggestions = await geocoding.AutocompleteAsync(query.Trim());
        return Results.Ok(suggestions);
    }
}
