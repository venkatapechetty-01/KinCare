using FluentValidation;
using KinCare.API.Endpoints;

namespace KinCare.API.Validators;

public class BookRideRequestValidator : AbstractValidator<BookRideRequest>
{
    public BookRideRequestValidator()
    {
        RuleFor(x => x.ResidentId)
            .Must(id => id == null || id != Guid.Empty)
            .WithMessage("ResidentId must be a valid non-empty GUID when provided.");

        RuleFor(x => x.PickupTime)
            .GreaterThan(DateTime.UtcNow)
            .WithMessage("Pickup time must be in the future.");

        RuleFor(x => x.PickupAddress).NotEmpty().MaximumLength(500);
        RuleFor(x => x.DestinationAddress).NotEmpty().MaximumLength(500);
    }
}

public class AdvanceStatusRequestValidator : AbstractValidator<AdvanceStatusRequest>
{
    public AdvanceStatusRequestValidator()
    {
        RuleFor(x => x.NewStatus).IsInEnum();
    }
}
