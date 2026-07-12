using FluentValidation;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/devices").WithTags("Devices").RequireAuthorization();

        group.MapPost("/register", RegisterDevice);
    }

    private static async Task<IResult> RegisterDevice(
        RegisterDeviceRequest request,
        IValidator<RegisterDeviceRequest> validator,
        HttpContext httpContext,
        AppDbContext db)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var userId = Guid.Parse(httpContext.User.FindFirst("sub")!.Value);

        var existing = await db.Set<DeviceRegistration>()
            .FirstOrDefaultAsync(d => d.UserId == userId && d.FcmToken == request.FcmToken);

        if (existing is not null)
        {
            existing.LastActiveAt = DateTime.UtcNow;
        }
        else
        {
            db.Set<DeviceRegistration>().Add(new DeviceRegistration
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FcmToken = request.FcmToken,
                DeviceName = request.DeviceName
            });
        }

        await db.SaveChangesAsync();
        return Results.Ok();
    }
}

public record RegisterDeviceRequest(string FcmToken, string? DeviceName);

public class RegisterDeviceRequestValidator : AbstractValidator<RegisterDeviceRequest>
{
    public RegisterDeviceRequestValidator()
    {
        RuleFor(x => x.FcmToken).NotEmpty().MaximumLength(500);
    }
}
