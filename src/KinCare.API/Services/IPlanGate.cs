using KinCare.API.Domain;

namespace KinCare.API.Services;

public interface IPlanGate
{
    void Requires(Organization org, PlanFeature feature);
}
