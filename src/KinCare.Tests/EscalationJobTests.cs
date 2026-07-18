using FluentAssertions;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Hubs;
using KinCare.API.Infrastructure;
using KinCare.API.Jobs;
using KinCare.API.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace KinCare.Tests;

// Regression coverage for Feature 6 (Escalation): before this session, FcmService existed
// but nothing ever called it — escalations fired a SignalR event and a RideEvent, but no
// push notification. These tests verify FCM is actually invoked (Moq on the now-virtual
// FcmService methods), not just that the escalation logic itself runs.
public class EscalationJobTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<FcmService> _mockFcm;
    private readonly EscalationJob _sut;

    public EscalationJobTests()
    {
        var dbName = "EscalationJobTests_" + Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new AppDbContext(options);

        var mockHub = new Mock<IHubContext<RideStatusHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockGroup = new Mock<IClientProxy>();
        mockHub.Setup(h => h.Clients).Returns(mockClients.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroup.Object);
        mockGroup
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockFcm = new Mock<FcmService>(_db, Options.Create(new FcmConfig()), NullLogger<FcmService>.Instance);
        _mockFcm.Setup(f => f.SendToFacilityUsersAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _sut = new EscalationJob(_db, mockHub.Object, _mockFcm.Object, NullLogger<EscalationJob>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private Facility SeedFacility()
    {
        var org = new Organization { Id = Guid.NewGuid(), Name = "Org", BillingEmail = "b@test.com", PlanTier = PlanTier.Professional, IsActive = true };
        _db.Organizations.Add(org);
        var facility = new Facility { Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "Facility", Address = "1 Rd", Timezone = "America/New_York" };
        _db.Facilities.Add(facility);
        _db.SaveChanges();
        return facility;
    }

    private Ride SeedRide(Guid facilityId, RideStatus status, DateTime pickupTime, DispatchChannel channel = DispatchChannel.SmsNemt)
    {
        var ride = new Ride
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            OrganizationId = _db.Facilities.First(f => f.Id == facilityId).OrganizationId,
            Status = status,
            DispatchChannel = channel,
            PickupTime = pickupTime,
            PickupAddress = "100 Pickup Ln",
            DestinationAddress = "200 Dest Dr"
        };
        _db.Rides.Add(ride);
        _db.SaveChanges();
        return ride;
    }

    [Fact]
    public async Task ExecuteAsync_DispatchedRide30MinPastPickup_SendsFcmPush()
    {
        var facility = SeedFacility();
        var ride = SeedRide(facility.Id, RideStatus.Dispatched, DateTime.UtcNow.AddMinutes(-31));

        await _sut.ExecuteAsync();

        _mockFcm.Verify(f => f.SendToFacilityUsersAsync(
            facility.Id,
            It.Is<string>(t => t.Contains("escalation", StringComparison.OrdinalIgnoreCase)),
            It.Is<string>(b => b.Contains("No confirmation"))),
            Times.Once);

        var events = _db.RideEvents.Where(e => e.RideId == ride.Id).ToList();
        events.Should().ContainSingle(e => e.TriggeredBy == "escalation_job");
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyEscalated_DoesNotSendFcmTwice()
    {
        var facility = SeedFacility();
        SeedRide(facility.Id, RideStatus.Dispatched, DateTime.UtcNow.AddMinutes(-31));

        await _sut.ExecuteAsync();
        await _sut.ExecuteAsync();

        _mockFcm.Verify(f => f.SendToFacilityUsersAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BrokerChannelRide_NeverEscalates()
    {
        var facility = SeedFacility();
        SeedRide(facility.Id, RideStatus.Dispatched, DateTime.UtcNow.AddMinutes(-31), DispatchChannel.Broker);

        await _sut.ExecuteAsync();

        _mockFcm.Verify(f => f.SendToFacilityUsersAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_RideWithinThreshold_DoesNotEscalate()
    {
        var facility = SeedFacility();
        // Dispatched only 5 minutes past pickup — well under the 30-minute threshold
        SeedRide(facility.Id, RideStatus.Dispatched, DateTime.UtcNow.AddMinutes(-5));

        await _sut.ExecuteAsync();

        _mockFcm.Verify(f => f.SendToFacilityUsersAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CompletedRide_NeverEscalates()
    {
        var facility = SeedFacility();
        SeedRide(facility.Id, RideStatus.Completed, DateTime.UtcNow.AddMinutes(-60));

        await _sut.ExecuteAsync();

        _mockFcm.Verify(f => f.SendToFacilityUsersAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
