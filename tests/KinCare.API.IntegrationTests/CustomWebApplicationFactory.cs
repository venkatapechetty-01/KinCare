using Hangfire;
using Hangfire.InMemory;
using KinCare.API.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KinCare.API.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Override configuration values for the test environment
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Disable endpoint-specific rate limiting
                ["IpRateLimit:EnableEndpointRateLimiting"] = "false",
                // Set an effectively unlimited general rule so no requests are blocked
                ["IpRateLimit:GeneralRules:0:Endpoint"] = "*",
                ["IpRateLimit:GeneralRules:0:Period"] = "1s",
                ["IpRateLimit:GeneralRules:0:Limit"] = "999999",
                // Clear the Twilio AuthToken so webhook handler skips signature validation
                ["Twilio:AuthToken"] = "",
            });
        });

        builder.ConfigureServices(services =>
        {
            var efDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true
                    || d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                    || d.ServiceType == typeof(DbContextOptions))
                .ToList();
            foreach (var d in efDescriptors)
                services.Remove(d);

            var dbName = "KinCareTestDb_" + Guid.NewGuid();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(dbName)
                       .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

            // Remove all Hangfire-related registrations and re-add with InMemory
            var hangfireDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("Hangfire") == true
                    || d.ImplementationType?.FullName?.Contains("Hangfire") == true)
                .ToList();
            foreach (var d in hangfireDescriptors)
                services.Remove(d);

            services.AddHangfire(config => config.UseInMemoryStorage());
            services.AddHangfireServer();

            // Set global storage so RecurringJob calls work
            GlobalConfiguration.Configuration.UseInMemoryStorage();

            services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Clear());
        });

        builder.UseEnvironment("Development");
    }
}
