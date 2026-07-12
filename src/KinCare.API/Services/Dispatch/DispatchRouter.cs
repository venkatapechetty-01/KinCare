using KinCare.API.Data;
using KinCare.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Services.Dispatch;

public class DispatchRouter
{
    private readonly AppDbContext _db;
    private readonly IPlanGate _planGate;

    public DispatchRouter(AppDbContext db, IPlanGate planGate)
    {
        _db = db;
        _planGate = planGate;
    }

    // Returns the channel and ALL eligible vendors (broadcast model).
    // For Uber/Broker channels vendor list will be empty — those APIs handle their own assignment.
    public async Task<(DispatchChannel channel, List<Vendor> vendors)> RouteAsync(
        Resident? resident,
        Organization org,
        Facility facility,
        DispatchChannel? preferredChannel = null)
    {
        var needsSpecialTransport = resident != null
            && (resident.NeedsWheelchair || resident.NeedsOxygen || resident.NeedsStretcher);

        if (preferredChannel.HasValue)
        {
            var preferred = preferredChannel.Value;

            if (needsSpecialTransport && preferred == DispatchChannel.SmsTaxi)
            {
                throw new InvalidOperationException(
                    "Selected transport mode cannot accommodate this resident's special needs. Please use NEMT.");
            }

            if (preferred == DispatchChannel.SmsNemt)
                return (DispatchChannel.SmsNemt, await FindVendors(facility.Id, DispatchMethod.SmsNemt));
            if (preferred == DispatchChannel.SmsTaxi)
                return (DispatchChannel.SmsTaxi, await FindVendors(facility.Id, DispatchMethod.SmsTaxi));
            if (preferred == DispatchChannel.Broker)
            {
                _planGate.Requires(org, PlanFeature.BrokerDispatch);
                return (DispatchChannel.Broker, new List<Vendor>());
            }
        }

        // Auto-routing
        if (needsSpecialTransport)
        {
            var nemtVendors = await FindVendors(facility.Id, DispatchMethod.SmsNemt);
            if (nemtVendors.Count > 0)
                return (DispatchChannel.SmsNemt, nemtVendors);

            if (org.BrokerEnabled && org.PlanTier >= PlanTier.Professional)
                return (DispatchChannel.Broker, new List<Vendor>());

            return (DispatchChannel.SmsNemt, new List<Vendor>());
        }

        var taxiVendors = await FindVendors(facility.Id, DispatchMethod.SmsTaxi);
        if (taxiVendors.Count > 0)
            return (DispatchChannel.SmsTaxi, taxiVendors);

        var smsNemtVendors = await FindVendors(facility.Id, DispatchMethod.SmsNemt);
        if (smsNemtVendors.Count > 0)
            return (DispatchChannel.SmsNemt, smsNemtVendors);

        if (org.BrokerEnabled && org.PlanTier >= PlanTier.Professional)
            return (DispatchChannel.Broker, new List<Vendor>());

        return (DispatchChannel.SmsNemt, new List<Vendor>());
    }

    private async Task<List<Vendor>> FindVendors(Guid facilityId, DispatchMethod method)
    {
        return await _db.Vendors
            .Where(v => v.FacilityId == facilityId
                && v.DispatchMethod == method
                && v.IsActive)
            .ToListAsync();
    }
}
