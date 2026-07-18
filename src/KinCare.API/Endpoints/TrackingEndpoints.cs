using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Hubs;
using KinCare.API.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Endpoints;

public static class TrackingEndpoints
{
    public static void MapTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/track/{token}", GetTrackingPage)
            .WithTags("Tracking")
            .AllowAnonymous();

        app.MapPost("/api/rides/location", UpdateLocation)
            .WithTags("Tracking")
            .AllowAnonymous();

        app.MapPost("/api/rides/track-status", UpdateTrackingStatus)
            .WithTags("Tracking")
            .AllowAnonymous();

        app.MapPost("/api/rides/track-accept", AcceptViaTrackingLink)
            .WithTags("Tracking")
            .AllowAnonymous();

        app.MapPost("/api/rides/track-decline", DeclineViaTrackingLink)
            .WithTags("Tracking")
            .AllowAnonymous();
    }

    // ── Stage metadata ────────────────────────────────────────────────────────

    private record StageInfo(RideStatus Status, string Icon, string Label, string ButtonLabel, string ButtonColor);

    private static readonly StageInfo[] Stages =
    [
        new(RideStatus.EnRoute,       "🚗", "On My Way",       "I'm on my way to facility",   "#1565c0"),
        new(RideStatus.Arrived,       "🏢", "Reached Facility", "I've arrived at the facility", "#6a1b9a"),
        new(RideStatus.PickedUp,      "👤", "Picked Up",        "Resident is in the vehicle",   "#e65100"),
        new(RideStatus.AtDestination, "🏥", "At Destination",   "Arrived at destination",       "#2e7d32"),
        new(RideStatus.Dropped,       "✅", "Dropped Off",      "Resident dropped off safely",  "#00695c"),
        // Return leg — NEMT round trips only. AwaitingReturn is deliberately absent
        // here: it's coordinator-triggered, never a driver-facing stage/one-tap action.
        new(RideStatus.ReturnEnRoute,  "🔄", "Returning",            "I'm on my way back",         "#1565c0"),
        new(RideStatus.ReturnPickedUp, "👤", "Picked Up (Return)",   "Resident is in the vehicle",  "#e65100"),
        new(RideStatus.Completed,     "🏁", "Trip Complete",    "Trip is complete",             "#37474f"),
    ];

    // ── GET /track/{token} ────────────────────────────────────────────────────

    private static async Task<IResult> GetTrackingPage(
        string token,
        HttpContext httpContext,
        AppDbContext db)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            return Results.Content(ErrorHtml("Invalid tracking link."), "text/html", statusCode: 400);

        // Set by SecurityHeadersMiddleware for every /track request — same value goes into
        // both the CSP header and this page's <script nonce="..."> tag.
        var nonce = httpContext.Items["CspNonce"] as string ?? "";

        // JSON data request from Angular dashboard
        if (httpContext.Request.Headers.Accept.ToString().Contains("application/json"))
        {
            var rideData = await db.Rides.AsNoTracking()
                .Include(r => r.Resident)
                .Include(r => r.Vendor)
                .FirstOrDefaultAsync(r => r.TrackingToken == token);

            if (rideData is null)
                return Results.NotFound(new { error = "Tracking link expired or invalid." });

            return Results.Ok(new TrackingPageDto(
                rideData.Status.ToString(),
                rideData.Resident is not null
                    ? $"{rideData.Resident.FirstName[0]}. {rideData.Resident.LastName}"
                    : "Resident",
                rideData.Vendor?.Name,
                rideData.PickupTime,
                rideData.LastKnownLat,
                rideData.LastKnownLng,
                rideData.LastLocationAt));
        }

        // HTML page for driver's phone browser
        var ride = await db.Rides.AsNoTracking()
            .Include(r => r.Resident)
            .Include(r => r.Facility)
            .FirstOrDefaultAsync(r => r.TrackingToken == token);

        if (ride is null)
        {
            // Not an active ride's token — the link may still be a pre-acceptance offer
            // token (every vendor gets one at broadcast time, before anyone has claimed
            // the ride). Same URL later resolves through the branch above once claimed.
            var offer = await db.RideDispatchOffers.AsNoTracking()
                .Include(o => o.Ride).ThenInclude(r => r.Resident)
                .Include(o => o.Vendor)
                .FirstOrDefaultAsync(o => o.TrackingToken == token);

            if (offer is null)
                return Results.Content(ErrorHtml("This tracking link has expired or is invalid."), "text/html", statusCode: 404);

            if (offer.Status != "Pending" || offer.Ride.Status != RideStatus.Dispatched)
                return Results.Content(AlreadyHandledHtml(offer.Status), "text/html");

            var offerResidentName = offer.Ride.Resident is not null
                ? $"{offer.Ride.Resident.FirstName} {offer.Ride.Resident.LastName}"
                : "Resident";
            var offerNeeds = BuildNeedsString(offer.Ride.Resident);

            return Results.Content(BuildAcceptHtml(token, offer.Ride, offerResidentName, offerNeeds, nonce), "text/html");
        }

        if (RideStateMachine.IsTerminal(ride.Status))
            return Results.Content(CompletedHtml(ride), "text/html");

        var residentName = ride.Resident is not null
            ? $"{ride.Resident.FirstName} {ride.Resident.LastName}"
            : "Resident";

        var needs = BuildNeedsString(ride.Resident);

        return Results.Content(BuildTrackerHtml(token, ride, residentName, needs, nonce), "text/html");
    }

    // ── POST /api/rides/location ──────────────────────────────────────────────

    private static async Task<IResult> UpdateLocation(
        UpdateLocationRequest request,
        AppDbContext db,
        IHubContext<RideStatusHub> hubContext)
    {
        if (string.IsNullOrWhiteSpace(request.TrackingToken) || request.TrackingToken.Length > 128)
            return Results.BadRequest();

        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            return Results.BadRequest(new { error = "Invalid coordinates." });

        var ride = await db.Rides.FirstOrDefaultAsync(r => r.TrackingToken == request.TrackingToken);
        if (ride is null) return Results.NotFound();
        if (RideStateMachine.IsTerminal(ride.Status))
            return Results.BadRequest(new { error = "Ride is no longer active." });

        var now = DateTime.UtcNow;
        ride.LastKnownLat = request.Latitude;
        ride.LastKnownLng = request.Longitude;
        ride.LastLocationAt = now;
        await db.SaveChangesAsync();

        await hubContext.Clients.Group($"facility:{ride.FacilityId}")
            .SendAsync("LocationUpdated", new
            {
                RideId = ride.Id,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                LastLocationAt = now,
            });

        return Results.Ok();
    }

    // ── POST /api/rides/track-status ─────────────────────────────────────────

    private static async Task<IResult> UpdateTrackingStatus(
        TrackingStatusRequest request,
        AppDbContext db,
        RideService rideService,
        FcmService fcm,
        ILogger<Program> logger)
    {
        if (string.IsNullOrWhiteSpace(request.TrackingToken) || request.TrackingToken.Length > 128)
            return Results.BadRequest(new { error = "Invalid token." });

        var ride = await db.Rides.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TrackingToken == request.TrackingToken);

        if (ride is null)
            return Results.NotFound(new { error = "Tracking link expired or invalid." });

        if (RideStateMachine.IsTerminal(ride.Status))
            return Results.BadRequest(new { error = "Ride is already completed or cancelled." });

        // Issue report — no status change, just appends an event
        if (request.NewStatus == "Issue")
        {
            db.RideEvents.Add(new RideEvent
            {
                Id = Guid.NewGuid(),
                RideId = ride.Id,
                FromStatus = ride.Status,
                ToStatus = ride.Status,
                TriggeredBy = "tracking_page",
                Notes = "Driver reported an issue via tracking page"
            });
            await db.SaveChangesAsync();
            logger.LogWarning("Issue reported via tracking page: RideId={RideId}", ride.Id);

            var residentName = ride.ResidentId.HasValue
                ? await db.Residents.Where(r => r.Id == ride.ResidentId.Value)
                    .Select(r => r.FirstName + " " + r.LastName).FirstOrDefaultAsync()
                : null;
            var vendorName = ride.VendorId.HasValue
                ? await db.Vendors.Where(v => v.Id == ride.VendorId.Value)
                    .Select(v => v.Name).FirstOrDefaultAsync()
                : null;
            try
            {
                await fcm.SendToFacilityUsersAsync(ride.FacilityId, "🚨 Issue reported",
                    $"{vendorName ?? "Driver"} reported an issue for {residentName ?? "resident"}");
            }
            catch (Exception ex) { logger.LogError(ex, "Failed to send FCM issue-report push for ride {RideId}", ride.Id); }

            return Results.Ok(new { message = "Issue reported." });
        }

        if (!Enum.TryParse<RideStatus>(request.NewStatus, out var newStatus))
            return Results.BadRequest(new { error = $"Unknown status: {request.NewStatus}" });

        try
        {
            await rideService.AdvanceStatusAsync(ride.Id, newStatus, "tracking_page");
            logger.LogInformation(
                "Tracking page advanced: RideId={RideId} → {Status}", ride.Id, newStatus);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        return Results.Ok(new { status = newStatus.ToString() });
    }

    // ── POST /api/rides/track-accept ─────────────────────────────────────────

    private static async Task<IResult> AcceptViaTrackingLink(
        TrackingTokenRequest request,
        AppDbContext db,
        RideService rideService,
        ILogger<Program> logger)
    {
        if (string.IsNullOrWhiteSpace(request.TrackingToken) || request.TrackingToken.Length > 128)
            return Results.BadRequest(new { error = "Invalid token." });

        var offer = await db.RideDispatchOffers.AsNoTracking()
            .FirstOrDefaultAsync(o => o.TrackingToken == request.TrackingToken);
        if (offer is null)
            return Results.NotFound(new { error = "This link has expired or is invalid." });

        var claimed = await rideService.ClaimRideAsync(offer.RideId, offer.VendorId, "tracking_link");
        if (!claimed)
            return Results.BadRequest(new { error = "This ride was already accepted by another driver." });

        logger.LogInformation("Ride accepted via tracking link: RideId={RideId} VendorId={VendorId}", offer.RideId, offer.VendorId);
        return Results.Ok(new { status = "Confirmed" });
    }

    // ── POST /api/rides/track-decline ────────────────────────────────────────

    private static async Task<IResult> DeclineViaTrackingLink(
        TrackingTokenRequest request,
        AppDbContext db,
        RideService rideService,
        ILogger<Program> logger)
    {
        if (string.IsNullOrWhiteSpace(request.TrackingToken) || request.TrackingToken.Length > 128)
            return Results.BadRequest(new { error = "Invalid token." });

        var offer = await db.RideDispatchOffers.AsNoTracking()
            .FirstOrDefaultAsync(o => o.TrackingToken == request.TrackingToken);
        if (offer is null)
            return Results.NotFound(new { error = "This link has expired or is invalid." });

        var declined = await rideService.DeclineOfferAsync(offer.RideId, offer.VendorId, "tracking_page", "Declined via link");
        if (!declined)
            return Results.BadRequest(new { error = "This ride is no longer available to decline." });

        logger.LogInformation("Ride declined via tracking link: RideId={RideId} VendorId={VendorId}", offer.RideId, offer.VendorId);
        return Results.Ok(new { status = "Declined" });
    }

    // ── HTML builders ─────────────────────────────────────────────────────────

    private static string BuildNeedsString(Resident? r)
    {
        if (r is null) return "";
        var needs = new List<string>();
        if (r.NeedsWheelchair) needs.Add("♿ Wheelchair");
        if (r.NeedsOxygen)     needs.Add("💨 Oxygen");
        if (r.NeedsStretcher)  needs.Add("🛏 Stretcher");
        if (r.NeedsWalker)     needs.Add("🦽 Walker");
        return needs.Count > 0 ? string.Join(" · ", needs) : "";
    }

    private static string BuildTrackerHtml(string token, Ride ride, string residentName, string needs, string nonce)
    {
        var currentStageIndex = Array.FindIndex(Stages, s => s.Status == ride.Status);
        if (currentStageIndex < 0) currentStageIndex = -1;

        var nextStage = RideStateMachine.NextTrackingStatus(ride.Status, ride.DispatchChannel);
        var nextStageInfo = nextStage.HasValue
            ? Array.Find(Stages, s => s.Status == nextStage.Value)
            : null;

        var progressDots = BuildProgressDots(currentStageIndex);
        var pickupUrl = "https://www.google.com/maps/dir/?api=1&destination=" + Uri.EscapeDataString(ride.PickupAddress);
        var destUrl   = "https://www.google.com/maps/dir/?api=1&destination=" + Uri.EscapeDataString(ride.DestinationAddress);
        var needsBadge = string.IsNullOrEmpty(needs) ? "" : $"<div class=\"needs\">{needs}</div>";
        var actionButton = nextStageInfo is not null
            ? $"<button class=\"btn-action\" style=\"background:{nextStageInfo.ButtonColor}\" data-next-status=\"{nextStage}\">"
              + $"<span class=\"btn-icon\">{nextStageInfo.Icon}</span>"
              + $"<span class=\"btn-text\">{nextStageInfo.ButtonLabel}</span></button>"
            : "";
        var pickupTimeStr = ride.PickupTime.ToString("ddd, MMM d · h:mm tt");

        // Use non-interpolated raw string; replace __PLACEHOLDERS__ to avoid $$ brace escaping issues
        var template = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
  <meta name="theme-color" content="#1565c0">
  <title>KinCare · Trip Tracker</title>
  <style>
    *{box-sizing:border-box;margin:0;padding:0;-webkit-tap-highlight-color:transparent}
    body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f0f4f8;min-height:100vh;padding:env(safe-area-inset-top,0) 0 env(safe-area-inset-bottom,0)}
    .header{background:#1565c0;color:#fff;padding:1rem 1.25rem 1.25rem;display:flex;align-items:center;gap:.75rem}
    .header-icon{font-size:1.8rem}
    .header-title{font-size:1.2rem;font-weight:700}
    .header-sub{font-size:.8rem;opacity:.8}
    .card{background:#fff;border-radius:16px;padding:1.25rem;margin:.75rem .75rem 0;box-shadow:0 1px 4px rgba(0,0,0,.08)}
    .resident-name{font-size:1.25rem;font-weight:700;color:#111;margin-bottom:.25rem}
    .needs{display:inline-flex;gap:.4rem;flex-wrap:wrap;margin-bottom:.75rem}
    .needs span{background:#fff3e0;color:#e65100;font-size:.8rem;font-weight:600;padding:.2rem .6rem;border-radius:20px}
    .info-row{display:flex;gap:.75rem;align-items:flex-start;padding:.55rem 0;border-bottom:1px solid #f0f0f0}
    .info-row:last-child{border-bottom:none}
    .info-icon{font-size:1.1rem;min-width:22px;padding-top:.05rem}
    .info-body .label{font-size:.7rem;color:#999;text-transform:uppercase;letter-spacing:.05em}
    .info-body .value{font-size:.95rem;color:#222;font-weight:500;margin-top:.1rem}
    .maps-link{display:inline-block;font-size:.8rem;color:#1565c0;text-decoration:none;margin-top:.25rem}
    .tracker-title{font-size:.8rem;color:#888;text-transform:uppercase;letter-spacing:.06em;margin-bottom:1rem}
    .stages{display:flex;align-items:flex-start;gap:0;overflow-x:auto;padding-bottom:.5rem}
    .stage{display:flex;flex-direction:column;align-items:center;flex:1;min-width:48px;position:relative}
    .stage:not(:last-child)::after{content:'';position:absolute;top:18px;left:calc(50% + 14px);right:calc(-50% + 14px);height:3px;background:#e0e0e0;z-index:0}
    .stage:not(:last-child).done::after{background:#2e7d32}
    .stage:not(:last-child).active::after{background:#e0e0e0}
    .dot{width:36px;height:36px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:1rem;background:#e0e0e0;color:#999;position:relative;z-index:1;transition:all .3s}
    .dot.done{background:#2e7d32;color:#fff;font-size:.9rem}
    .dot.active{background:#1565c0;color:#fff;box-shadow:0 0 0 4px rgba(21,101,192,.2);animation:pulse 1.5s ease-in-out infinite}
    .stage-label{font-size:.65rem;color:#999;text-align:center;margin-top:.4rem;line-height:1.2;max-width:52px}
    .stage-label.done{color:#2e7d32;font-weight:600}
    .stage-label.active{color:#1565c0;font-weight:700}
    .btn-action{display:flex;align-items:center;justify-content:center;gap:.75rem;width:100%;padding:1.1rem;border:none;border-radius:14px;font-size:1.1rem;font-weight:700;color:#fff;cursor:pointer;transition:transform .1s,opacity .15s;margin-bottom:.75rem}
    .btn-action:active{transform:scale(.97);opacity:.85}
    .btn-icon{font-size:1.4rem}
    .btn-report{display:flex;align-items:center;justify-content:center;gap:.5rem;width:100%;padding:.8rem;border:2px solid #e53935;border-radius:12px;background:#fff;color:#e53935;font-size:.95rem;font-weight:600;cursor:pointer}
    .btn-report:active{background:#ffebee}
    .gps-bar{display:flex;align-items:center;gap:.4rem;padding:.6rem 1rem;background:#e8f5e9;border-radius:10px;margin-top:.5rem}
    .gps-dot{width:8px;height:8px;border-radius:50%;background:#4caf50;animation:pulse 1.5s infinite}
    .gps-text{font-size:.8rem;color:#2e7d32;font-weight:500}
    .toast{position:fixed;bottom:2rem;left:50%;transform:translateX(-50%);background:#323232;color:#fff;padding:.75rem 1.5rem;border-radius:10px;font-size:.9rem;font-weight:500;opacity:0;transition:opacity .3s;pointer-events:none;white-space:nowrap;z-index:999}
    .toast.show{opacity:1}
    @keyframes pulse{0%,100%{opacity:1}50%{opacity:.4}}
  </style>
</head>
<body>
<div class="header">
  <div class="header-icon">🚐</div>
  <div>
    <div class="header-title">KinCare Trip Tracker</div>
    <div class="header-sub">Live status · GPS active</div>
  </div>
</div>
<div class="card">
  <div class="resident-name">__RESIDENT__</div>
  __NEEDS_BADGE__
  <div class="info-row">
    <div class="info-icon">📍</div>
    <div class="info-body">
      <div class="label">Pickup address</div>
      <div class="value">__PICKUP_ADDR__</div>
      <a class="maps-link" href="__PICKUP_URL__" target="_blank">Open in Google Maps ↗</a>
    </div>
  </div>
  <div class="info-row">
    <div class="info-icon">🏥</div>
    <div class="info-body">
      <div class="label">Destination</div>
      <div class="value">__DEST_ADDR__</div>
      <a class="maps-link" href="__DEST_URL__" target="_blank">Open in Google Maps ↗</a>
    </div>
  </div>
  <div class="info-row">
    <div class="info-icon">⏰</div>
    <div class="info-body">
      <div class="label">Scheduled pickup</div>
      <div class="value">__PICKUP_TIME__</div>
    </div>
  </div>
</div>
<div class="card">
  <div class="tracker-title">Trip Progress</div>
  <div class="stages" id="stages">__PROGRESS_DOTS__</div>
</div>
<div class="card" id="action-card">
  __ACTION_BUTTON__
  <button class="btn-report" id="report-btn">⚠️ Report a problem</button>
  <div class="gps-bar" id="gps-bar">
    <div class="gps-dot"></div>
    <div class="gps-text" id="gps-text">Waiting for GPS…</div>
  </div>
</div>
<div class="toast" id="toast"></div>
<script nonce="__NONCE__">
  const TOKEN = '__TOKEN__';
  const API   = window.location.origin;
  let currentStatus = '__STATUS__';

  function sendLocation(lat, lng) {
    fetch(API + '/api/rides/location', {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({ trackingToken: TOKEN, latitude: lat, longitude: lng })
    }).catch(function() {});
  }

  if ('geolocation' in navigator) {
    navigator.geolocation.watchPosition(
      function(pos) {
        var lat = pos.coords.latitude, lng = pos.coords.longitude;
        document.getElementById('gps-text').textContent =
          'GPS active · ' + lat.toFixed(4) + ', ' + lng.toFixed(4);
        sendLocation(lat, lng);
      },
      function() { document.getElementById('gps-text').textContent = 'GPS unavailable'; },
      { enableHighAccuracy: true, maximumAge: 30000, timeout: 15000 }
    );
  } else {
    document.getElementById('gps-text').textContent = 'GPS not supported on this device';
  }

  async function advance(newStatus) {
    var btn = document.querySelector('.btn-action');
    if (btn) { btn.disabled = true; btn.style.opacity = '.6'; }
    try {
      var res = await fetch(API + '/api/rides/track-status', {
        method: 'POST',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify({ trackingToken: TOKEN, newStatus: newStatus })
      });
      var data = await res.json().catch(function() { return {}; });
      if (res.ok) {
        currentStatus = newStatus;
        showToast('✅ Status updated!');
        setTimeout(function() { window.location.reload(); }, 1200);
      } else {
        showToast(data.error || 'Could not update status. Try again.');
        if (btn) { btn.disabled = false; btn.style.opacity = '1'; }
      }
    } catch(e) {
      showToast('Network error. Check your connection.');
      if (btn) { btn.disabled = false; btn.style.opacity = '1'; }
    }
  }

  async function reportIssue() {
    await fetch(API + '/api/rides/track-status', {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({ trackingToken: TOKEN, newStatus: 'Issue' })
    });
    showToast('⚠️ Issue reported to coordinator');
  }

  function showToast(msg) {
    var t = document.getElementById('toast');
    t.textContent = msg;
    t.classList.add('show');
    setTimeout(function() { t.classList.remove('show'); }, 3000);
  }

  var actionBtn = document.querySelector('.btn-action');
  if (actionBtn) {
    actionBtn.addEventListener('click', function() { advance(actionBtn.dataset.nextStatus); });
  }
  var reportBtn = document.getElementById('report-btn');
  if (reportBtn) {
    reportBtn.addEventListener('click', reportIssue);
  }
</script>
</body>
</html>
""";

        return template
            .Replace("__RESIDENT__",    residentName)
            .Replace("__NEEDS_BADGE__", needsBadge)
            .Replace("__PICKUP_ADDR__", System.Web.HttpUtility.HtmlEncode(ride.PickupAddress))
            .Replace("__PICKUP_URL__",  pickupUrl)
            .Replace("__DEST_ADDR__",   System.Web.HttpUtility.HtmlEncode(ride.DestinationAddress))
            .Replace("__DEST_URL__",    destUrl)
            .Replace("__PICKUP_TIME__", pickupTimeStr)
            .Replace("__PROGRESS_DOTS__", progressDots)
            .Replace("__ACTION_BUTTON__", actionButton)
            .Replace("__TOKEN__",       token)
            .Replace("__STATUS__",      ride.Status.ToString())
            .Replace("__NONCE__",       nonce);
    }

    private static string BuildProgressDots(int currentStageIndex)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < Stages.Length; i++)
        {
            var s = Stages[i];
            string dotClass, labelClass, dotContent;
            if (i < currentStageIndex)      { dotClass = "done";   labelClass = "done";   dotContent = "✓"; }
            else if (i == currentStageIndex) { dotClass = "active"; labelClass = "active"; dotContent = s.Icon; }
            else                             { dotClass = "";       labelClass = "";       dotContent = s.Icon; }

            var stageClass = i < currentStageIndex ? "stage done" : (i == currentStageIndex ? "stage active" : "stage");
            sb.Append($"""<div class="{stageClass}"><div class="dot {dotClass}">{dotContent}</div><div class="stage-label {labelClass}">{s.Label}</div></div>""");
        }
        return sb.ToString();
    }

    private static string CompletedHtml(Ride ride) => """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <title>KinCare · Trip Complete</title>
          <style>
            body{font-family:-apple-system,BlinkMacSystemFont,sans-serif;background:#f0f4f8;display:flex;align-items:center;justify-content:center;min-height:100vh;padding:1rem}
            .card{background:#fff;border-radius:16px;padding:2rem;text-align:center;box-shadow:0 2px 8px rgba(0,0,0,.1);max-width:380px;width:100%}
            .icon{font-size:4rem;margin-bottom:1rem}
            h2{color:#2e7d32;font-size:1.4rem;margin-bottom:.5rem}
            p{color:#666;font-size:.95rem}
          </style>
        </head>
        <body>
          <div class="card">
            <div class="icon">🏁</div>
            <h2>Trip Complete</h2>
            <p>This ride has been completed. Thank you for using KinCare.</p>
          </div>
        </body>
        </html>
        """;

    private static string BuildAcceptHtml(string token, Ride ride, string residentName, string needs, string nonce)
    {
        var needsBadge = string.IsNullOrEmpty(needs) ? "" : $"<div class=\"needs\">{needs}</div>";
        var pickupUrl = "https://www.google.com/maps/dir/?api=1&destination=" + Uri.EscapeDataString(ride.PickupAddress);
        var destUrl   = "https://www.google.com/maps/dir/?api=1&destination=" + Uri.EscapeDataString(ride.DestinationAddress);
        var pickupTimeStr = ride.PickupTime.ToString("ddd, MMM d · h:mm tt");

        var template = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
  <meta name="theme-color" content="#1565c0">
  <title>KinCare · New Ride Request</title>
  <style>
    *{box-sizing:border-box;margin:0;padding:0;-webkit-tap-highlight-color:transparent}
    body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f0f4f8;min-height:100vh;padding:env(safe-area-inset-top,0) 0 env(safe-area-inset-bottom,0)}
    .header{background:#1565c0;color:#fff;padding:1rem 1.25rem 1.25rem;display:flex;align-items:center;gap:.75rem}
    .header-icon{font-size:1.8rem}
    .header-title{font-size:1.2rem;font-weight:700}
    .header-sub{font-size:.8rem;opacity:.8}
    .card{background:#fff;border-radius:16px;padding:1.25rem;margin:.75rem .75rem 0;box-shadow:0 1px 4px rgba(0,0,0,.08)}
    .resident-name{font-size:1.25rem;font-weight:700;color:#111;margin-bottom:.25rem}
    .needs{display:inline-flex;gap:.4rem;flex-wrap:wrap;margin-bottom:.75rem}
    .needs span{background:#fff3e0;color:#e65100;font-size:.8rem;font-weight:600;padding:.2rem .6rem;border-radius:20px}
    .info-row{display:flex;gap:.75rem;align-items:flex-start;padding:.55rem 0;border-bottom:1px solid #f0f0f0}
    .info-row:last-child{border-bottom:none}
    .info-icon{font-size:1.1rem;min-width:22px;padding-top:.05rem}
    .info-body .label{font-size:.7rem;color:#999;text-transform:uppercase;letter-spacing:.05em}
    .info-body .value{font-size:.95rem;color:#222;font-weight:500;margin-top:.1rem}
    .maps-link{display:inline-block;font-size:.8rem;color:#1565c0;text-decoration:none;margin-top:.25rem}
    .btn-accept{display:flex;align-items:center;justify-content:center;gap:.75rem;width:100%;padding:1.1rem;border:none;border-radius:14px;font-size:1.15rem;font-weight:700;color:#fff;cursor:pointer;background:#2e7d32;transition:transform .1s,opacity .15s;margin-bottom:.6rem}
    .btn-accept:active{transform:scale(.97);opacity:.85}
    .btn-decline{display:flex;align-items:center;justify-content:center;gap:.5rem;width:100%;padding:.8rem;border:2px solid #e53935;border-radius:12px;background:#fff;color:#e53935;font-size:.95rem;font-weight:600;cursor:pointer}
    .btn-decline:active{background:#ffebee}
    .toast{position:fixed;bottom:2rem;left:50%;transform:translateX(-50%);background:#323232;color:#fff;padding:.75rem 1.5rem;border-radius:10px;font-size:.9rem;font-weight:500;opacity:0;transition:opacity .3s;pointer-events:none;white-space:nowrap;z-index:999}
    .toast.show{opacity:1}
  </style>
</head>
<body>
<div class="header">
  <div class="header-icon">🚐</div>
  <div>
    <div class="header-title">New Ride Request</div>
    <div class="header-sub">Reply below or text ACCEPT / DECLINE</div>
  </div>
</div>
<div class="card">
  <div class="resident-name">__RESIDENT__</div>
  __NEEDS_BADGE__
  <div class="info-row">
    <div class="info-icon">📍</div>
    <div class="info-body">
      <div class="label">Pickup address</div>
      <div class="value">__PICKUP_ADDR__</div>
      <a class="maps-link" href="__PICKUP_URL__" target="_blank">Open in Google Maps ↗</a>
    </div>
  </div>
  <div class="info-row">
    <div class="info-icon">🏥</div>
    <div class="info-body">
      <div class="label">Destination</div>
      <div class="value">__DEST_ADDR__</div>
      <a class="maps-link" href="__DEST_URL__" target="_blank">Open in Google Maps ↗</a>
    </div>
  </div>
  <div class="info-row">
    <div class="info-icon">⏰</div>
    <div class="info-body">
      <div class="label">Scheduled pickup</div>
      <div class="value">__PICKUP_TIME__</div>
    </div>
  </div>
</div>
<div class="card">
  <button class="btn-accept" id="accept-btn">
    <span>✅</span><span>Accept This Ride</span>
  </button>
  <button class="btn-decline" id="decline-btn">✕ Can't Take This One</button>
</div>
<div class="toast" id="toast"></div>
<script nonce="__NONCE__">
  const TOKEN = '__TOKEN__';
  const API   = window.location.origin;

  async function accept() {
    document.querySelectorAll('button').forEach(function(b) { b.disabled = true; b.style.opacity = '.6'; });
    try {
      var res = await fetch(API + '/api/rides/track-accept', {
        method: 'POST',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify({ trackingToken: TOKEN })
      });
      var data = await res.json().catch(function() { return {}; });
      if (res.ok) {
        showToast('✅ Ride accepted!');
        setTimeout(function() { window.location.reload(); }, 900);
      } else {
        showToast(data.error || 'Could not accept. It may have been claimed already.');
        setTimeout(function() { window.location.reload(); }, 1500);
      }
    } catch(e) {
      showToast('Network error. Check your connection.');
      document.querySelectorAll('button').forEach(function(b) { b.disabled = false; b.style.opacity = '1'; });
    }
  }

  async function decline() {
    document.querySelectorAll('button').forEach(function(b) { b.disabled = true; b.style.opacity = '.6'; });
    try {
      await fetch(API + '/api/rides/track-decline', {
        method: 'POST',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify({ trackingToken: TOKEN })
      });
    } catch(e) {}
    showToast('Got it — thanks for letting us know.');
    setTimeout(function() { window.location.reload(); }, 900);
  }

  function showToast(msg) {
    var t = document.getElementById('toast');
    t.textContent = msg;
    t.classList.add('show');
    setTimeout(function() { t.classList.remove('show'); }, 3000);
  }

  document.getElementById('accept-btn').addEventListener('click', accept);
  document.getElementById('decline-btn').addEventListener('click', decline);
</script>
</body>
</html>
""";

        return template
            .Replace("__RESIDENT__",    residentName)
            .Replace("__NEEDS_BADGE__", needsBadge)
            .Replace("__PICKUP_ADDR__", System.Web.HttpUtility.HtmlEncode(ride.PickupAddress))
            .Replace("__PICKUP_URL__",  pickupUrl)
            .Replace("__DEST_ADDR__",   System.Web.HttpUtility.HtmlEncode(ride.DestinationAddress))
            .Replace("__DEST_URL__",    destUrl)
            .Replace("__PICKUP_TIME__", pickupTimeStr)
            .Replace("__TOKEN__",       token)
            .Replace("__NONCE__",       nonce);
    }

    private static string AlreadyHandledHtml(string offerStatus)
    {
        var (icon, title, message) = offerStatus switch
        {
            "Declined" => ("👍", "You Declined This Ride", "No further action needed — thanks for letting us know."),
            "Superseded" => ("🚐", "Already Taken", "Another driver already accepted this ride."),
            _ => ("⚠️", "No Longer Available", "This ride request is no longer available."),
        };

        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <title>KinCare</title>
          <style>
            body{font-family:-apple-system,BlinkMacSystemFont,sans-serif;background:#f0f4f8;display:flex;align-items:center;justify-content:center;min-height:100vh;padding:1rem}
            .card{background:#fff;border-radius:16px;padding:2rem;text-align:center;box-shadow:0 2px 8px rgba(0,0,0,.1);max-width:380px;width:100%}
            .icon{font-size:3rem;margin-bottom:1rem}
            h2{color:#333;font-size:1.2rem;margin-bottom:.5rem}
            p{color:#666;font-size:.9rem}
          </style>
        </head>
        <body>
          <div class="card">
            <div class="icon">{{icon}}</div>
            <h2>{{title}}</h2>
            <p>{{message}}</p>
          </div>
        </body>
        </html>
        """;
    }

    private static string ErrorHtml(string message) => $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <title>KinCare</title>
          <style>
            body{font-family:-apple-system,BlinkMacSystemFont,sans-serif;background:#f0f4f8;display:flex;align-items:center;justify-content:center;min-height:100vh;padding:1rem}
            .card{background:#fff;border-radius:16px;padding:2rem;text-align:center;box-shadow:0 2px 8px rgba(0,0,0,.1);max-width:380px;width:100%}
            .icon{font-size:3rem;margin-bottom:1rem}
            h2{color:#555;font-size:1.2rem}
          </style>
        </head>
        <body>
          <div class="card">
            <div class="icon">⚠️</div>
            <h2>{{message}}</h2>
          </div>
        </body>
        </html>
        """;
}

public record TrackingPageDto(
    string Status, string ResidentName, string? VendorName,
    DateTime PickupTime, double? Latitude, double? Longitude, DateTime? LastLocationAt);

public record UpdateLocationRequest(string TrackingToken, double Latitude, double Longitude);
public record TrackingStatusRequest(string TrackingToken, string NewStatus);
public record TrackingTokenRequest(string TrackingToken);
