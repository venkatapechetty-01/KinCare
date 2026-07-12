using FluentAssertions;
using KinCare.API.Domain;
using KinCare.API.Services;

namespace KinCare.Tests;

public class RideStateMachineTests
{
    private readonly RideStateMachine _sut = new();

    [Theory]
    [InlineData(RideStatus.Dispatched,    RideStatus.Confirmed)]
    [InlineData(RideStatus.Dispatched,    RideStatus.Cancelled)]
    [InlineData(RideStatus.Confirmed,     RideStatus.EnRoute)]
    [InlineData(RideStatus.Confirmed,     RideStatus.Cancelled)]
    [InlineData(RideStatus.EnRoute,       RideStatus.Arrived)]
    [InlineData(RideStatus.EnRoute,       RideStatus.Cancelled)]
    [InlineData(RideStatus.Arrived,       RideStatus.PickedUp)]
    [InlineData(RideStatus.Arrived,       RideStatus.Cancelled)]
    [InlineData(RideStatus.PickedUp,      RideStatus.AtDestination)]
    [InlineData(RideStatus.PickedUp,      RideStatus.Cancelled)]
    [InlineData(RideStatus.AtDestination, RideStatus.Dropped)]
    [InlineData(RideStatus.AtDestination, RideStatus.Cancelled)]
    [InlineData(RideStatus.Dropped,       RideStatus.Completed)]
    [InlineData(RideStatus.Dropped,       RideStatus.Cancelled)]
    public void CanTransition_ValidTransitions_ReturnsTrue(RideStatus from, RideStatus to)
    {
        _sut.CanTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(RideStatus.Dispatched,    RideStatus.EnRoute)]
    [InlineData(RideStatus.Dispatched,    RideStatus.Arrived)]
    [InlineData(RideStatus.Dispatched,    RideStatus.Dropped)]
    [InlineData(RideStatus.Dispatched,    RideStatus.Completed)]
    [InlineData(RideStatus.Confirmed,     RideStatus.Arrived)]
    [InlineData(RideStatus.Confirmed,     RideStatus.Dropped)]
    [InlineData(RideStatus.Confirmed,     RideStatus.Completed)]
    [InlineData(RideStatus.EnRoute,       RideStatus.Confirmed)]
    [InlineData(RideStatus.EnRoute,       RideStatus.Dropped)]
    [InlineData(RideStatus.EnRoute,       RideStatus.Completed)]
    [InlineData(RideStatus.Arrived,       RideStatus.Confirmed)]
    [InlineData(RideStatus.Arrived,       RideStatus.EnRoute)]
    [InlineData(RideStatus.Arrived,       RideStatus.Dropped)]
    [InlineData(RideStatus.Arrived,       RideStatus.Completed)]
    [InlineData(RideStatus.PickedUp,      RideStatus.Arrived)]
    [InlineData(RideStatus.PickedUp,      RideStatus.Dropped)]
    [InlineData(RideStatus.PickedUp,      RideStatus.Completed)]
    [InlineData(RideStatus.AtDestination, RideStatus.PickedUp)]
    [InlineData(RideStatus.AtDestination, RideStatus.Completed)]
    [InlineData(RideStatus.Dropped,       RideStatus.Dispatched)]
    [InlineData(RideStatus.Completed,     RideStatus.Dispatched)]
    [InlineData(RideStatus.Completed,     RideStatus.Cancelled)]
    [InlineData(RideStatus.Cancelled,     RideStatus.Dispatched)]
    [InlineData(RideStatus.Cancelled,     RideStatus.Confirmed)]
    public void CanTransition_InvalidTransitions_ReturnsFalse(RideStatus from, RideStatus to)
    {
        _sut.CanTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public void Validate_ValidTransition_DoesNotThrow()
    {
        var act = () => _sut.Validate(RideStatus.Dispatched, RideStatus.Confirmed);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_InvalidTransition_ThrowsInvalidOperationException()
    {
        var act = () => _sut.Validate(RideStatus.Completed, RideStatus.Dispatched);
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
            _sut.CanTransition(RideStatus.Completed, target).Should().BeFalse(
                $"Completed should not transition to {target}");
        }
    }

    [Fact]
    public void CanTransition_CancelledStatus_CannotTransitionToAnything()
    {
        var allStatuses = Enum.GetValues<RideStatus>();
        foreach (var target in allStatuses)
        {
            _sut.CanTransition(RideStatus.Cancelled, target).Should().BeFalse(
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
            _sut.CanTransition(status, RideStatus.Cancelled).Should().BeTrue(
                $"{status} should be cancellable");
        }
    }
}
