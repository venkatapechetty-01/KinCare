using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KinCare.API.Data;
using KinCare.API.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace KinCare.API.IntegrationTests;

public class TwilioWebhookIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TwilioWebhookIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Helper: post a Twilio-style form payload ──────────────────────────────

    private static FormUrlEncodedContent TwilioForm(string from, string body, string? sid = null) =>
        new(new Dictionary<string, string>
        {
            ["MessageSid"] = sid ?? "SM" + Guid.NewGuid().ToString("N"),
            ["From"] = from,
            ["Body"] = body
        });

    // ── Helper: seed org + facility + vendor directly in DB ───────────────────

    private async Task<(Guid OrgId, Guid FacilityId, Guid VendorId)> SeedOrgFacilityVendorAsync(
        string phoneNumber)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = $"Twilio Test Org {Guid.NewGuid():N}",
            BillingEmail = $"billing-{Guid.NewGuid():N}@test.com",
            PlanTier = PlanTier.Starter,
            IsActive = true
        };
        db.Organizations.Add(org);

        var facility = new Facility
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Twilio Test Facility",
            Address = "123 Test St, Detroit, MI",
            Timezone = "America/New_York",
            IsActive = true
        };
        db.Facilities.Add(facility);

        var vendor = new Vendor
        {
            Id = Guid.NewGuid(),
            FacilityId = facility.Id,
            Name = "Test Vendor",
            PhoneNumber = phoneNumber,
            VendorType = VendorType.Wheelchair,
            DispatchMethod = DispatchMethod.SmsNemt,
            CapabilityTier = VendorCapabilityTier.Basic,
            IsActive = true
        };
        db.Vendors.Add(vendor);

        await db.SaveChangesAsync();
        return (org.Id, facility.Id, vendor.Id);
    }

    // Helper: seed a Dispatched ride with a Pending offer for the given vendor
    private async Task<Guid> SeedRideWithOfferAsync(Guid orgId, Guid facilityId, Guid vendorId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ride = new Ride
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            OrganizationId = orgId,
            Status = RideStatus.Dispatched,
            DispatchChannel = DispatchChannel.SmsNemt,
            PickupTime = DateTime.UtcNow.AddHours(2),
            PickupAddress = "123 Main St, Detroit, MI",
            DestinationAddress = "456 Oak Ave, Detroit, MI"
        };
        db.Rides.Add(ride);

        db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            FromStatus = RideStatus.Dispatched,
            ToStatus = RideStatus.Dispatched,
            TriggeredBy = "system",
            Notes = "Seeded for test"
        });

        db.RideDispatchOffers.Add(new RideDispatchOffer
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            VendorId = vendorId,
            Status = "Pending"
        });

        await db.SaveChangesAsync();
        return ride.Id;
    }

    // Helper: seed a ride already assigned to the given vendor, at an arbitrary status
    private async Task<Guid> SeedAssignedRideAsync(
        Guid orgId, Guid facilityId, Guid vendorId, RideStatus status, DispatchChannel channel = DispatchChannel.SmsNemt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ride = new Ride
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            OrganizationId = orgId,
            VendorId = vendorId,
            Status = status,
            DispatchChannel = channel,
            PickupTime = DateTime.UtcNow.AddHours(2),
            PickupAddress = "123 Main St, Detroit, MI",
            DestinationAddress = "456 Oak Ave, Detroit, MI"
        };
        db.Rides.Add(ride);

        db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            FromStatus = status,
            ToStatus = status,
            TriggeredBy = "system",
            Notes = "Seeded for test"
        });

        await db.SaveChangesAsync();
        return ride.Id;
    }

    private async Task<RideStatus> GetRideStatusAsync(Guid rideId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ride = await db.Rides.FindAsync(rideId);
        return ride!.Status;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnknownVendorPhone_Returns200()
    {
        // Webhook must always return 200 even when vendor is unknown
        var response = await _client.PostAsync("/webhook/twilio",
            TwilioForm("+15550009999", "1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task KnownVendorReply1_AcceptsRide_Returns200()
    {
        var phone = $"+1555{Guid.NewGuid().ToString("N")[..7]}";
        var (orgId, facilityId, vendorId) = await SeedOrgFacilityVendorAsync(phone);
        await SeedRideWithOfferAsync(orgId, facilityId, vendorId);

        var response = await _client.PostAsync("/webhook/twilio",
            TwilioForm(phone, "1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task DuplicateMessageSid_ProcessedOnce_Returns200()
    {
        var phone = $"+1555{Guid.NewGuid().ToString("N")[..7]}";
        var (orgId, facilityId, vendorId) = await SeedOrgFacilityVendorAsync(phone);
        await SeedRideWithOfferAsync(orgId, facilityId, vendorId);

        var sid = "SM" + Guid.NewGuid().ToString("N");
        var form1 = TwilioForm(phone, "1", sid);
        var form2 = TwilioForm(phone, "1", sid);

        var first = await _client.PostAsync("/webhook/twilio", form1);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second request with the same MessageSid must also return 200 without error
        var second = await _client.PostAsync("/webhook/twilio", form2);
        second.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 on duplicate SID but got {second.StatusCode}: {await second.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task InvalidBody_NonDigit_Returns200()
    {
        // Handler parses the first character as a digit; non-digits are ignored gracefully
        var response = await _client.PostAsync("/webhook/twilio",
            TwilioForm("+15550001234", "HELLO"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Reply2_Decline_Returns200()
    {
        var phone = $"+1555{Guid.NewGuid().ToString("N")[..7]}";
        var (orgId, facilityId, vendorId) = await SeedOrgFacilityVendorAsync(phone);
        await SeedRideWithOfferAsync(orgId, facilityId, vendorId);

        var response = await _client.PostAsync("/webhook/twilio",
            TwilioForm(phone, "2"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    // ── Round-trip return leg (NEMT only) ────────────────────────────────────

    [Fact]
    public async Task Reply8AtDropped_OneWayRide_CompletesRide_RegressionCheck()
    {
        var phone = $"+1555{Guid.NewGuid().ToString("N")[..7]}";
        var (orgId, facilityId, vendorId) = await SeedOrgFacilityVendorAsync(phone);
        var rideId = await SeedAssignedRideAsync(orgId, facilityId, vendorId, RideStatus.Dropped);

        var response = await _client.PostAsync("/webhook/twilio", TwilioForm(phone, "8"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetRideStatusAsync(rideId)).Should().Be(RideStatus.Completed);
    }

    [Fact]
    public async Task Reply3AtAwaitingReturn_AdvancesToReturnEnRoute()
    {
        var phone = $"+1555{Guid.NewGuid().ToString("N")[..7]}";
        var (orgId, facilityId, vendorId) = await SeedOrgFacilityVendorAsync(phone);
        var rideId = await SeedAssignedRideAsync(orgId, facilityId, vendorId, RideStatus.AwaitingReturn);

        var response = await _client.PostAsync("/webhook/twilio", TwilioForm(phone, "3"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetRideStatusAsync(rideId)).Should().Be(RideStatus.ReturnEnRoute);
    }

    [Fact]
    public async Task Reply5AtReturnEnRoute_AdvancesToReturnPickedUp()
    {
        var phone = $"+1555{Guid.NewGuid().ToString("N")[..7]}";
        var (orgId, facilityId, vendorId) = await SeedOrgFacilityVendorAsync(phone);
        var rideId = await SeedAssignedRideAsync(orgId, facilityId, vendorId, RideStatus.ReturnEnRoute);

        var response = await _client.PostAsync("/webhook/twilio", TwilioForm(phone, "5"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetRideStatusAsync(rideId)).Should().Be(RideStatus.ReturnPickedUp);
    }

    [Fact]
    public async Task Reply8AtReturnPickedUp_CompletesRoundTripRide()
    {
        var phone = $"+1555{Guid.NewGuid().ToString("N")[..7]}";
        var (orgId, facilityId, vendorId) = await SeedOrgFacilityVendorAsync(phone);
        var rideId = await SeedAssignedRideAsync(orgId, facilityId, vendorId, RideStatus.ReturnPickedUp);

        var response = await _client.PostAsync("/webhook/twilio", TwilioForm(phone, "8"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetRideStatusAsync(rideId)).Should().Be(RideStatus.Completed);
    }

    [Fact]
    public async Task OutOfContextDigit_AtAwaitingReturn_IsSafeNoOp()
    {
        // Digit 4 has no meaning while AwaitingReturn (that's an outbound-leg digit) — must be a no-op, not an error
        var phone = $"+1555{Guid.NewGuid().ToString("N")[..7]}";
        var (orgId, facilityId, vendorId) = await SeedOrgFacilityVendorAsync(phone);
        var rideId = await SeedAssignedRideAsync(orgId, facilityId, vendorId, RideStatus.AwaitingReturn);

        var response = await _client.PostAsync("/webhook/twilio", TwilioForm(phone, "4"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetRideStatusAsync(rideId)).Should().Be(RideStatus.AwaitingReturn);
    }
}
