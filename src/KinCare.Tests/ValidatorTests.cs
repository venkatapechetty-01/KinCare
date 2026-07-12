using FluentAssertions;
using FluentValidation.TestHelper;
using KinCare.API.Domain;
using KinCare.API.Endpoints;
using KinCare.API.Validators;

namespace KinCare.Tests;

public class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _sut = new();

    [Fact]
    public void Valid_Request_Passes()
    {
        var result = _sut.TestValidate(new LoginRequest("user@test.com", "password123"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_Email_Fails()
    {
        var result = _sut.TestValidate(new LoginRequest("", "password123"));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Invalid_Email_Fails()
    {
        var result = _sut.TestValidate(new LoginRequest("not-an-email", "password123"));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Empty_Password_Fails()
    {
        var result = _sut.TestValidate(new LoginRequest("user@test.com", ""));
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }
}

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _sut = new();

    [Fact]
    public void Valid_Request_Passes()
    {
        var request = new RegisterRequest(
            "My Org", "Main Facility", "123 Main St",
            "John", "Doe", "john@test.com", "ValidPass1!");
        var result = _sut.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("", "Org name required")]
    public void Empty_OrganizationName_Fails(string orgName, string _)
    {
        var request = new RegisterRequest(
            orgName, "Facility", "123 St", "John", "Doe", "j@t.com", "pass1234");
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.OrganizationName);
    }

    [Fact]
    public void Empty_FacilityName_Fails()
    {
        var request = new RegisterRequest(
            "Org", "", "123 St", "John", "Doe", "j@t.com", "pass1234");
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.FacilityName);
    }

    [Fact]
    public void Empty_FacilityAddress_Fails()
    {
        var request = new RegisterRequest(
            "Org", "Facility", "", "John", "Doe", "j@t.com", "pass1234");
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.FacilityAddress);
    }

    [Fact]
    public void Empty_FirstName_Fails()
    {
        var request = new RegisterRequest(
            "Org", "Facility", "123 St", "", "Doe", "j@t.com", "pass1234");
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.FirstName);
    }

    [Fact]
    public void Empty_LastName_Fails()
    {
        var request = new RegisterRequest(
            "Org", "Facility", "123 St", "John", "", "j@t.com", "pass1234");
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.LastName);
    }

    [Fact]
    public void Invalid_Email_Fails()
    {
        var request = new RegisterRequest(
            "Org", "Facility", "123 St", "John", "Doe", "not-email", "pass1234");
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Short_Password_Fails()
    {
        var request = new RegisterRequest(
            "Org", "Facility", "123 St", "John", "Doe", "j@t.com", "short");
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void TooLong_OrganizationName_Fails()
    {
        var request = new RegisterRequest(
            new string('A', 201), "Facility", "123 St", "John", "Doe", "j@t.com", "pass1234");
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.OrganizationName);
    }
}

public class BookRideRequestValidatorTests
{
    private readonly BookRideRequestValidator _sut = new();

    [Fact]
    public void Valid_Request_Passes()
    {
        var request = new BookRideRequest(
            Guid.NewGuid(), Guid.NewGuid(),
            DateTime.UtcNow.AddHours(1), "123 Pickup St", "456 Dest Ave", null);
        var result = _sut.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_ResidentId_Fails()
    {
        var request = new BookRideRequest(
            Guid.NewGuid(), Guid.Empty,
            DateTime.UtcNow.AddHours(1), "123 Pickup St", "456 Dest Ave", null);
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ResidentId);
    }

    [Fact]
    public void PastPickupTime_Fails()
    {
        var request = new BookRideRequest(
            Guid.NewGuid(), Guid.NewGuid(),
            DateTime.UtcNow.AddHours(-1), "123 Pickup St", "456 Dest Ave", null);
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.PickupTime);
    }

    [Fact]
    public void Empty_PickupAddress_Fails()
    {
        var request = new BookRideRequest(
            Guid.NewGuid(), Guid.NewGuid(),
            DateTime.UtcNow.AddHours(1), "", "456 Dest Ave", null);
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.PickupAddress);
    }

    [Fact]
    public void Empty_DestinationAddress_Fails()
    {
        var request = new BookRideRequest(
            Guid.NewGuid(), Guid.NewGuid(),
            DateTime.UtcNow.AddHours(1), "123 Pickup St", "", null);
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DestinationAddress);
    }
}

public class CreateResidentRequestValidatorTests
{
    private readonly CreateResidentRequestValidator _sut = new();

    [Fact]
    public void Valid_Request_Passes()
    {
        var request = new CreateResidentRequest(
            Guid.NewGuid(), "Jane", "Doe", false, false, false, false, "Some notes");
        var result = _sut.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_FirstName_Fails()
    {
        var request = new CreateResidentRequest(
            Guid.NewGuid(), "", "Doe", false, false, false, false, null);
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.FirstName);
    }

    [Fact]
    public void Empty_LastName_Fails()
    {
        var request = new CreateResidentRequest(
            Guid.NewGuid(), "Jane", "", false, false, false, false, null);
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.LastName);
    }

    [Fact]
    public void TooLong_DriverNotes_Fails()
    {
        var request = new CreateResidentRequest(
            Guid.NewGuid(), "Jane", "Doe", false, false, false, false, new string('N', 1001));
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DriverNotes);
    }

    [Fact]
    public void Null_DriverNotes_Passes()
    {
        var request = new CreateResidentRequest(
            Guid.NewGuid(), "Jane", "Doe", true, true, false, false, null);
        var result = _sut.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}

public class CreateVendorRequestValidatorTests
{
    private readonly CreateVendorRequestValidator _sut = new();

    [Fact]
    public void Valid_Request_Passes()
    {
        var request = new CreateVendorRequest(
            Guid.NewGuid(), "ABC Taxi", "555-1234",
            VendorType.Ambulatory, DispatchMethod.SmsTaxi, VendorCapabilityTier.Basic);
        var result = _sut.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_Name_Fails()
    {
        var request = new CreateVendorRequest(
            Guid.NewGuid(), "", "555-1234",
            VendorType.Ambulatory, DispatchMethod.SmsTaxi, VendorCapabilityTier.Basic);
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Empty_PhoneNumber_Fails()
    {
        var request = new CreateVendorRequest(
            Guid.NewGuid(), "ABC Taxi", "",
            VendorType.Ambulatory, DispatchMethod.SmsTaxi, VendorCapabilityTier.Basic);
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Fact]
    public void Invalid_VendorType_Fails()
    {
        var request = new CreateVendorRequest(
            Guid.NewGuid(), "ABC Taxi", "555-1234",
            (VendorType)999, DispatchMethod.SmsTaxi, VendorCapabilityTier.Basic);
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.VendorType);
    }

    [Fact]
    public void Invalid_DispatchMethod_Fails()
    {
        var request = new CreateVendorRequest(
            Guid.NewGuid(), "ABC Taxi", "555-1234",
            VendorType.Ambulatory, (DispatchMethod)999, VendorCapabilityTier.Basic);
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.DispatchMethod);
    }
}

public class AdvanceStatusRequestValidatorTests
{
    private readonly AdvanceStatusRequestValidator _sut = new();

    [Theory]
    [InlineData(RideStatus.Confirmed)]
    [InlineData(RideStatus.EnRoute)]
    [InlineData(RideStatus.Arrived)]
    [InlineData(RideStatus.Dropped)]
    [InlineData(RideStatus.Completed)]
    [InlineData(RideStatus.Cancelled)]
    public void Valid_Status_Passes(RideStatus status)
    {
        var request = new AdvanceStatusRequest(status, null);
        var result = _sut.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Invalid_Status_Fails()
    {
        var request = new AdvanceStatusRequest((RideStatus)999, null);
        var result = _sut.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.NewStatus);
    }
}
