using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Data;

public class AppDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Resident> Residents => Set<Resident>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Ride> Rides => Set<Ride>();
    public DbSet<RideEvent> RideEvents => Set<RideEvent>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DeviceRegistration> DeviceRegistrations => Set<DeviceRegistration>();
    public DbSet<RideDispatchOffer> RideDispatchOffers => Set<RideDispatchOffer>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    private readonly IHttpContextAccessor? _httpContextAccessor;

    public AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor? httpContextAccessor = null)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    // These properties are evaluated per-query (EF Core captures them as closures)
    private Guid? CurrentFacilityId =>
        (_httpContextAccessor?.HttpContext?.Items["TenantContext"] as TenantContext)?.FacilityId;

    private Guid? CurrentOrganizationId =>
        (_httpContextAccessor?.HttpContext?.Items["TenantContext"] as TenantContext)?.OrganizationId;

    private bool IsSuperAdmin =>
        (_httpContextAccessor?.HttpContext?.Items["TenantContext"] as TenantContext)?.Role == UserRole.SuperAdmin;

    private bool IsOrgAdmin =>
        (_httpContextAccessor?.HttpContext?.Items["TenantContext"] as TenantContext)?.Role == UserRole.OrgAdmin;

    private bool HasTenantContext =>
        _httpContextAccessor?.HttpContext?.Items["TenantContext"] != null;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureOrganization(builder);
        ConfigureFacility(builder);
        ConfigureAppUser(builder);
        ConfigureInvitation(builder);
        ConfigureResident(builder);
        ConfigureVendor(builder);
        ConfigureRide(builder);
        ConfigureRideEvent(builder);
        ConfigureRefreshToken(builder);
        ConfigureDeviceRegistration(builder);
        ConfigureRideDispatchOffer(builder);
        ConfigurePasswordResetToken(builder);
    }

    private static void ConfigureOrganization(ModelBuilder builder)
    {
        builder.Entity<Organization>(e =>
        {
            e.ToTable("organizations");
            e.HasKey(o => o.Id);
            e.Property(o => o.Name).HasMaxLength(200).IsRequired();
            e.Property(o => o.BillingEmail).HasMaxLength(200).IsRequired();
            e.Property(o => o.StripeCustomerId).HasMaxLength(100);
            e.Property(o => o.StripeSubscriptionId).HasMaxLength(100);
            e.Property(o => o.PlanTier).HasConversion<string>().HasMaxLength(20);
        });
    }

    private static void ConfigureFacility(ModelBuilder builder)
    {
        builder.Entity<Facility>(e =>
        {
            e.ToTable("facilities");
            e.HasKey(f => f.Id);
            e.Property(f => f.Name).HasMaxLength(200).IsRequired();
            e.Property(f => f.Address).HasMaxLength(500).IsRequired();
            e.Property(f => f.Timezone).HasMaxLength(50).IsRequired();

            e.HasOne(f => f.Organization)
                .WithMany(o => o.Facilities)
                .HasForeignKey(f => f.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(f => f.OrganizationId);
        });
    }

    private static void ConfigureAppUser(ModelBuilder builder)
    {
        builder.Entity<AppUser>(e =>
        {
            e.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
            e.Property(u => u.LastName).HasMaxLength(100).IsRequired();
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);

            e.HasOne(u => u.Organization)
                .WithMany(o => o.Users)
                .HasForeignKey(u => u.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(u => u.Facility)
                .WithMany(f => f.Users)
                .HasForeignKey(u => u.FacilityId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureInvitation(ModelBuilder builder)
    {
        builder.Entity<Invitation>(e =>
        {
            e.ToTable("invitations");
            e.HasKey(i => i.Id);
            e.Property(i => i.Email).HasMaxLength(200).IsRequired();
            e.Property(i => i.Token).HasMaxLength(100).IsRequired();
            e.Property(i => i.Role).HasConversion<string>().HasMaxLength(20);

            e.HasOne(i => i.Organization)
                .WithMany(o => o.Invitations)
                .HasForeignKey(i => i.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(i => i.Facility)
                .WithMany()
                .HasForeignKey(i => i.FacilityId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(i => i.Token).IsUnique();
        });
    }

    private void ConfigureResident(ModelBuilder builder)
    {
        builder.Entity<Resident>(e =>
        {
            e.ToTable("residents");
            e.HasKey(r => r.Id);
            e.Property(r => r.FirstName).HasMaxLength(100).IsRequired();
            e.Property(r => r.LastName).HasMaxLength(100).IsRequired();
            e.Property(r => r.DriverNotes).HasMaxLength(1000);

            e.HasOne(r => r.Facility)
                .WithMany(f => f.Residents)
                .HasForeignKey(r => r.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);

            // Coordinator sees only their facility; OrgAdmin sees all facilities in their org
            e.HasQueryFilter(r =>
                !HasTenantContext ||
                IsSuperAdmin ||
                (IsOrgAdmin && Set<Facility>().Any(f => f.Id == r.FacilityId && f.OrganizationId == CurrentOrganizationId)) ||
                (!IsOrgAdmin && r.FacilityId == CurrentFacilityId));
        });
    }

    private void ConfigureVendor(ModelBuilder builder)
    {
        builder.Entity<Vendor>(e =>
        {
            e.ToTable("vendors");
            e.HasKey(v => v.Id);
            e.Property(v => v.Name).HasMaxLength(200).IsRequired();
            e.Property(v => v.PhoneNumber).HasMaxLength(20).IsRequired();
            e.Property(v => v.VendorType).HasConversion<string>().HasMaxLength(20);
            e.Property(v => v.DispatchMethod).HasConversion<string>().HasMaxLength(20);
            e.Property(v => v.CapabilityTier).HasConversion<string>().HasMaxLength(20);
            e.Property(v => v.Company).HasMaxLength(200);
            e.Property(v => v.ServiceArea).HasMaxLength(200);
            e.Property(v => v.PhotoUrl).HasMaxLength(500);

            e.HasOne(v => v.Facility)
                .WithMany(f => f.Vendors)
                .HasForeignKey(v => v.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(v => v.PhoneNumber);

            // Same tenant scoping as Resident
            e.HasQueryFilter(v =>
                !HasTenantContext ||
                IsSuperAdmin ||
                (IsOrgAdmin && Set<Facility>().Any(f => f.Id == v.FacilityId && f.OrganizationId == CurrentOrganizationId)) ||
                (!IsOrgAdmin && v.FacilityId == CurrentFacilityId));
        });
    }

    private void ConfigureRide(ModelBuilder builder)
    {
        builder.Entity<Ride>(e =>
        {
            e.ToTable("rides");
            e.HasKey(r => r.Id);
            e.Property(r => r.PickupAddress).HasMaxLength(500).IsRequired();
            e.Property(r => r.DestinationAddress).HasMaxLength(500).IsRequired();
            e.Property(r => r.ExternalTripId).HasMaxLength(200);
            e.Property(r => r.TrackingToken).HasMaxLength(100);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(r => r.DispatchChannel).HasConversion<string>().HasMaxLength(20);

            e.HasOne(r => r.Facility)
                .WithMany(f => f.Rides)
                .HasForeignKey(r => r.FacilityId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(r => r.Organization)
                .WithMany()
                .HasForeignKey(r => r.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.Resident)
                .WithMany(res => res.Rides)
                .HasForeignKey(r => r.ResidentId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(r => r.Vendor)
                .WithMany(v => v.Rides)
                .HasForeignKey(r => r.VendorId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(r => new { r.FacilityId, r.PickupTime });
            e.HasIndex(r => new { r.DispatchChannel, r.Status });
            e.HasIndex(r => r.TrackingToken)
                .HasFilter("\"TrackingToken\" IS NOT NULL");
            e.HasIndex(r => r.ExternalTripId)
                .HasFilter("\"ExternalTripId\" IS NOT NULL");

            // Tenant scoping on rides
            e.HasQueryFilter(r =>
                !HasTenantContext ||
                IsSuperAdmin ||
                (IsOrgAdmin && r.OrganizationId == CurrentOrganizationId) ||
                (!IsOrgAdmin && r.FacilityId == CurrentFacilityId));
        });
    }

    private void ConfigureRideEvent(ModelBuilder builder)
    {
        builder.Entity<RideEvent>(e =>
        {
            e.ToTable("ride_events");
            e.HasKey(re => re.Id);
            e.Property(re => re.TriggeredBy).HasMaxLength(100).IsRequired();
            e.Property(re => re.Notes).HasMaxLength(1000);
            e.Property(re => re.FromStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(re => re.ToStatus).HasConversion<string>().HasMaxLength(30);

            e.HasOne(re => re.Ride)
                .WithMany(r => r.Events)
                .HasForeignKey(re => re.RideId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(re => new { re.RideId, re.OccurredAt });
            e.HasIndex(re => new { re.RideId, re.TriggeredBy });

            // Follow same filter as Ride — RideEvents are always accessed via a ride
            e.HasQueryFilter(re =>
                !HasTenantContext ||
                IsSuperAdmin ||
                (IsOrgAdmin && Set<Ride>().Any(r => r.Id == re.RideId && r.OrganizationId == CurrentOrganizationId)) ||
                (!IsOrgAdmin && Set<Ride>().Any(r => r.Id == re.RideId && r.FacilityId == CurrentFacilityId)));
        });
    }

    private static void ConfigureRefreshToken(ModelBuilder builder)
    {
        builder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(rt => rt.Id);
            e.Property(rt => rt.Token).HasMaxLength(200).IsRequired();
            e.Property(rt => rt.ReplacedByToken).HasMaxLength(200);

            e.HasOne(rt => rt.User)
                .WithMany()
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(rt => rt.Token).IsUnique();
            e.HasIndex(rt => rt.UserId);
        });
    }

    private static void ConfigureDeviceRegistration(ModelBuilder builder)
    {
        builder.Entity<DeviceRegistration>(e =>
        {
            e.ToTable("device_registrations");
            e.HasKey(d => d.Id);
            e.Property(d => d.FcmToken).HasMaxLength(500).IsRequired();
            e.Property(d => d.DeviceName).HasMaxLength(100);

            e.HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(d => new { d.UserId, d.FcmToken }).IsUnique();
        });
    }

    private static void ConfigurePasswordResetToken(ModelBuilder builder)
    {
        builder.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("password_reset_tokens");
            e.HasKey(p => p.Id);
            e.Property(p => p.Token).HasMaxLength(200).IsRequired();

            e.HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(p => p.Token).IsUnique();
            e.HasIndex(p => p.UserId);
        });
    }

    private void ConfigureRideDispatchOffer(ModelBuilder builder)
    {
        builder.Entity<RideDispatchOffer>(e =>
        {
            e.ToTable("ride_dispatch_offers");
            e.HasKey(o => o.Id);
            e.Property(o => o.Status).HasMaxLength(20).IsRequired();
            e.Property(o => o.TrackingToken).HasMaxLength(100);

            e.HasOne(o => o.Ride)
                .WithMany()
                .HasForeignKey(o => o.RideId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(o => o.Vendor)
                .WithMany()
                .HasForeignKey(o => o.VendorId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(o => new { o.RideId, o.VendorId }).IsUnique();
            e.HasIndex(o => new { o.VendorId, o.Status });
            e.HasIndex(o => o.TrackingToken)
                .HasFilter("\"TrackingToken\" IS NOT NULL");

            // Follow same filter as Ride
            e.HasQueryFilter(o =>
                !HasTenantContext ||
                IsSuperAdmin ||
                (IsOrgAdmin && Set<Ride>().Any(r => r.Id == o.RideId && r.OrganizationId == CurrentOrganizationId)) ||
                (!IsOrgAdmin && Set<Ride>().Any(r => r.Id == o.RideId && r.FacilityId == CurrentFacilityId)));
        });
    }
}
