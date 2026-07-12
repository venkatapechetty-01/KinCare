using KinCare.API.Domain;

namespace KinCare.API.Services;

public class PlanGate : IPlanGate
{
    private static readonly Dictionary<PlanFeature, PlanTier> MinimumTiers = new()
    {
        { PlanFeature.SmartVendorTracking, PlanTier.Professional },
        { PlanFeature.CsvExport, PlanTier.Professional },
        { PlanFeature.OrgDashboard, PlanTier.Professional },
        { PlanFeature.BrokerDispatch, PlanTier.Professional },
    };

    public void Requires(Organization org, PlanFeature feature)
    {
        if (!MinimumTiers.TryGetValue(feature, out var required))
            return;

        if (org.PlanTier < required)
            throw new PlanGateException(feature, required);
    }
}

public class PlanGateException : Exception
{
    public PlanFeature Feature { get; }
    public PlanTier RequiredTier { get; }

    public PlanGateException(PlanFeature feature, PlanTier requiredTier)
        : base($"Feature '{feature}' requires plan tier '{requiredTier}' or higher.")
    {
        Feature = feature;
        RequiredTier = requiredTier;
    }
}
