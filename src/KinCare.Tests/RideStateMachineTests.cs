using FluentAssertions;
using KinCare.API.Domain;
using KinCare.API.Services;

namespace KinCare.Tests;

public class RideStateMachineTests
{
    private readonly RideStateMachine _sut = new();

    [Theory]
    [InlineData(RideStatus.Dispatched,    RideStatus.Confirmed,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Dispatched,    RideStatus.Cancelled,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Confirmed,     RideStatus.EnRoute,       DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Confirmed,     RideStatus.Cancelled,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.EnRoute,       RideStatus.Arrived,       DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.EnRoute,       RideStatus.Cancelled,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Arrived,       RideStatus.PickedUp,      DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Arrived,       RideStatus.Cancelled,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.PickedUp,      RideStatus.AtDestination, DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.PickedUp,      RideStatus.Cancelled,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.AtDestination, RideStatus.Dropped,       DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.AtDestination, RideStatus.Cancelled,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Dropped,       RideStatus.Completed,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Dropped,       RideStatus.Cancelled,     DispatchChannel.SmsNemt)]
    // Round-trip return leg — SmsNemt only
    [InlineData(RideStatus.Dropped,       RideStatus.AwaitingReturn,  DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.AwaitingReturn, RideStatus.ReturnEnRoute,  DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.AwaitingReturn, RideStatus.Cancelled,      DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.ReturnEnRoute,  RideStatus.ReturnPickedUp, DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.ReturnEnRoute,  RideStatus.Cancelled,      DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.ReturnPickedUp, RideStatus.Completed,      DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.ReturnPickedUp, RideStatus.Cancelled,      DispatchChannel.SmsNemt)]
    public void CanTransition_ValidTransitions_ReturnsTrue(RideStatus from, RideStatus to, DispatchChannel channel)
    {
        _sut.CanTransition(from, to, channel).Should().BeTrue();
    }

    [Theory]
    [InlineData(RideStatus.Dispatched,    RideStatus.EnRoute,       DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Dispatched,    RideStatus.Arrived,       DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Dispatched,    RideStatus.Dropped,       DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Dispatched,    RideStatus.Completed,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Confirmed,     RideStatus.Arrived,       DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Confirmed,     RideStatus.Dropped,       DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Confirmed,     RideStatus.Completed,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.EnRoute,       RideStatus.Confirmed,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.EnRoute,       RideStatus.Dropped,       DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.EnRoute,       RideStatus.Completed,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Arrived,       RideStatus.Confirmed,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Arrived,       RideStatus.EnRoute,       DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Arrived,       RideStatus.Dropped,       DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Arrived,       RideStatus.Completed,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.PickedUp,      RideStatus.Arrived,       DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.PickedUp,      RideStatus.Dropped,       DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.PickedUp,      RideStatus.Completed,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.AtDestination, RideStatus.PickedUp,      DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.AtDestination, RideStatus.Completed,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Dropped,       RideStatus.Dispatched,    DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Completed,     RideStatus.Dispatched,    DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Completed,     RideStatus.Cancelled,     DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Cancelled,     RideStatus.Dispatched,    DispatchChannel.SmsNemt)]
    [InlineData(RideStatus.Cancelled,     RideStatus.Confirmed,     DispatchChannel.SmsNemt)]
    // Round-trip return leg is SmsNemt-only — every other channel is rejected at the gate
    [InlineData(RideStatus.Dropped,       RideStatus.AwaitingReturn, DispatchChannel.SmsTaxi)]
    [InlineData(RideStatus.Dropped,       RideStatus.AwaitingReturn, DispatchChannel.Broker)]
    public void CanTransition_InvalidTransitions_ReturnsFalse(RideStatus from, RideStatus to, DispatchChannel channel)
    {
        _sut.CanTransition(from, to, channel).Should().BeFalse();
    }

    [Fact]
    public void Validate_ValidTransition_DoesNotThrow()
    {
        var act = () => _sut.Validate(RideStatus.Dispatched, RideStatus.Confirmed, DispatchChannel.SmsNemt);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_InvalidTransition_ThrowsInvalidOperationException()
    {
        var act = () => _sut.Validate(RideStatus.Completed, RideStatus.Dispatched, DispatchChannel.SmsNemt);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Completed*Dispatched*");
    }

    [Theory]
    [InlineData(RideStatus.Completed, true)]
    [InlineData(RideStatus.Cancelled, true)]
    [InlineData(RideStatus.Dispatched, false)]
    [InlineData(RideStatus.Confirmed, false)]
    [InlineData(RideStatus.EnRoute, false)]
    [InlineData(RideStatus.Arrived, false)]
    [InlineData(RideStatus.Dropped, false)]
    [InlineData(RideStatus.AwaitingReturn, false)]
    [InlineData(RideStatus.ReturnEnRoute, false)]
    [InlineData(RideStatus.ReturnPickedUp, false)]
    public void IsTerminal_ReturnsCorrectValue(RideStatus status, bool expected)
    {
        RideStateMachine.IsTerminal(status).Should().Be(expected);
    }

    [Fact]
    public void CanTransition_CompletedStatus_CannotTransitionToAnything()
    {
        var allStatuses = Enum.GetValues<RideStatus>();
        foreach (var target in allStatuses)
        {
            _sut.CanTransition(RideStatus.Completed, target, DispatchChannel.SmsNemt).Should().BeFalse(
                $"Completed should not transition to {target}");
        }
    }

    [Fact]
    public void CanTransition_CancelledStatus_CannotTransitionToAnything()
    {
        var allStatuses = Enum.GetValues<RideStatus>();
        foreach (var target in allStatuses)
        {
            _sut.CanTransition(RideStatus.Cancelled, target, DispatchChannel.SmsNemt).Should().BeFalse(
                $"Cancelled should not transition to {target}");
        }
    }

    [Fact]
    public void CanTransition_EveryNonTerminalStatus_CanBeCancelled()
    {
        var nonTerminal = Enum.GetValues<RideStatus>()
            .Where(s => !RideStateMachine.IsTerminal(s));

        foreach (var status in nonTerminal)
        {
            _sut.CanTransition(status, RideStatus.Cancelled, DispatchChannel.SmsNemt).Should().BeTrue(
                $"{status} should be cancellable");
        }
    }
}
