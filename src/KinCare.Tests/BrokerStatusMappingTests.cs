using FluentAssertions;
using KinCare.API.Domain;
using KinCare.API.Services.Dispatch;

namespace KinCare.Tests;

public class BrokerStatusMappingTests
{
    [Theory]
    [InlineData("assigned", RideStatus.Confirmed)]
    [InlineData("en_route", RideStatus.EnRoute)]
    [InlineData("at_pickup", RideStatus.Arrived)]
    [InlineData("completed", RideStatus.Dropped)]
    [InlineData("cancelled", RideStatus.Cancelled)]
    public void MapBrokerStatus_KnownStatuses_MapsCorrectly(string brokerStatus, RideStatus expected)
    {
        BrokerDispatchService.MapBrokerStatus(brokerStatus).Should().Be(expected);
    }

    [Theory]
    [InlineData("ASSIGNED", RideStatus.Confirmed)]
    [InlineData("En_Route", RideStatus.EnRoute)]
    [InlineData("AT_PICKUP", RideStatus.Arrived)]
    public void MapBrokerStatus_CaseInsensitive(string brokerStatus, RideStatus expected)
    {
        BrokerDispatchService.MapBrokerStatus(brokerStatus).Should().Be(expected);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("queued")]
    [InlineData("")]
    public void MapBrokerStatus_UnknownStatuses_ReturnsNull(string brokerStatus)
    {
        BrokerDispatchService.MapBrokerStatus(brokerStatus).Should().BeNull();
    }
}
