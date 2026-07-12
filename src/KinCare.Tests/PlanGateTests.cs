using FluentAssertions;
using KinCare.API.Domain;
using KinCare.API.Services;

namespace KinCare.Tests;

public class PlanGateTests
{
    private readonly PlanGate _sut = new();

    [Theory]
    [InlineData(PlanFeature.SmartVendorTracking, PlanTier.Professional)]
    [InlineData(PlanFeature.CsvExport, PlanTier.Professional)]
    [InlineData(PlanFeature.OrgDashboard, PlanTier.Professional)]
    [InlineData(PlanFeature.BrokerDispatch, PlanTier.Professional)]
    public void Requires_StarterOrg_ProfessionalFeature_Throws(PlanFeature feature, PlanTier required)
    {
        var org = new Organization { PlanTier = PlanTier.Starter };

        var act = () => _sut.Requires(org, feature);

        act.Should().Throw<PlanGateException>()
            .Which.RequiredTier.Should().Be(required);
    }

    [Theory]
    [InlineData(PlanFeature.SmartVendorTracking)]
    [InlineData(PlanFeature.CsvExport)]
    [InlineData(PlanFeature.OrgDashboard)]
    [InlineData(PlanFeature.BrokerDispatch)]
    public void Requires_ProfessionalOrg_ProfessionalFeature_DoesNotThrow(PlanFeature feature)
    {
        var org = new Organization { PlanTier = PlanTier.Professional };

        var act = () => _sut.Requires(org, feature);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(PlanFeature.SmartVendorTracking)]
    [InlineData(PlanFeature.CsvExport)]
    [InlineData(PlanFeature.OrgDashboard)]
    [InlineData(PlanFeature.BrokerDispatch)]
    public void Requires_EnterpriseOrg_AllFeatures_DoesNotThrow(PlanFeature feature)
    {
        var org = new Organization { PlanTier = PlanTier.Enterprise };

        var act = () => _sut.Requires(org, feature);

        act.Should().NotThrow();
    }

    [Fact]
    public void PlanGateException_ContainsCorrectProperties()
    {
        var org = new Organization { PlanTier = PlanTier.Starter };

        try
        {
            _sut.Requires(org, PlanFeature.BrokerDispatch);
        }
        catch (PlanGateException ex)
        {
            ex.RequiredTier.Should().Be(PlanTier.Professional);
            ex.Message.Should().Contain("Professional");
            return;
        }

        Assert.Fail("Expected PlanGateException was not thrown.");
    }
}
