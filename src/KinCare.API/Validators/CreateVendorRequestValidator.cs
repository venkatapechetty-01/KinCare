using FluentValidation;
using KinCare.API.Endpoints;

namespace KinCare.API.Validators;

public class CreateVendorRequestValidator : AbstractValidator<CreateVendorRequest>
{
    public CreateVendorRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.VendorType).IsInEnum();
        RuleFor(x => x.DispatchMethod).IsInEnum();
        RuleFor(x => x.CapabilityTier).IsInEnum();
    }
}

public class UpdateVendorRequestValidator : AbstractValidator<UpdateVendorRequest>
{
    public UpdateVendorRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.VendorType).IsInEnum();
        RuleFor(x => x.DispatchMethod).IsInEnum();
        RuleFor(x => x.CapabilityTier).IsInEnum();
    }
}
