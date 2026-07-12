using System.IdentityModel.Tokens.Jwt;
using System.Text;
using AspNetCoreRateLimit;
using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using KinCare.API.Endpoints;
using KinCare.API.Hubs;
using KinCare.API.Jobs;
using KinCare.API.Services;
using KinCare.API.Webhooks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Splunk;

// Disable automatic claim type mapping so custom claims like "role" work correctly
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

// Serilog — structured logging to console + Splunk HEC
var splunkConfig = builder.Configuration.GetSection("Splunk");
var splunkHecUrl = splunkConfig["HecUrl"];      // e.g. https://your-splunk:8088/services/collector
var splunkToken  = splunkConfig["HecToken"];    // HEC token from Splunk Settings > Data Inputs > HTTP Event Collector

var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .Enrich.WithProperty("Application", "KinCare.API")
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");

if (!string.IsNullOrWhiteSpace(splunkHecUrl) && !string.IsNullOrWhiteSpace(splunkToken))
{
    loggerConfig.WriteTo.EventCollector(
        splunkHecUrl,
        splunkToken,
        sourceType: "kincare_api",
        restrictedToMinimumLevel: LogEventLevel.Information);
}

Log.Logger = loggerConfig.CreateLogger();
builder.Host.UseSerilog();

// Configuration
builder.Services.Configure<JwtConfig>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("App"));
builder.Services.Configure<TwilioConfig>(builder.Configuration.GetSection("Twilio"));
builder.Services.Configure<FcmConfig>(builder.Configuration.GetSection("Firebase"));
builder.Services.Configure<StripeConfig>(builder.Configuration.GetSection("Stripe"));
builder.Services.Configure<BrokerConfig>(builder.Configuration.GetSection("Broker"));
builder.Services.Configure<SendGridConfig>(builder.Configuration.GetSection("SendGrid"));

// HttpContextAccessor — needed by RlsSessionInterceptor
builder.Services.AddHttpContextAccessor();

// Database
builder.Services.AddScoped<RlsSessionInterceptor>();
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.AddInterceptors(serviceProvider.GetRequiredService<RlsSessionInterceptor>());
});

// Identity
builder.Services.AddIdentityCore<AppUser>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 12;

    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// JWT Authentication
var jwtConfig = builder.Configuration.GetSection("Jwt").Get<JwtConfig>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtConfig.Issuer,
            ValidAudience = jwtConfig.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig.SecretKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = "role",
            NameClaimType = JwtRegisteredClaimNames.Sub
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Global camelCase JSON serialization
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Application Services
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<IPlanGate, PlanGate>();
builder.Services.AddScoped<RideStateMachine>();
builder.Services.AddScoped<RideService>();
builder.Services.AddScoped<KinCare.API.Services.Dispatch.DispatchRouter>();
builder.Services.AddScoped<KinCare.API.Services.Dispatch.TwilioDispatchService>();
builder.Services.AddScoped<KinCare.API.Services.Dispatch.BrokerDispatchService>();
builder.Services.AddScoped<FcmService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<KinCare.API.Jobs.EscalationJob>();
builder.Services.AddScoped<KinCare.API.Jobs.CheckpointReminderJob>();
builder.Services.AddScoped<KinCare.API.Jobs.ExternalTripSyncJob>();
builder.Services.AddHttpClient();

// Hangfire
builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(options =>
        options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")!)));
builder.Services.AddHangfireServer();

// SignalR
builder.Services.AddSignalR();

// CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:4200" };
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Rate Limiting
builder.Services.AddRateLimiting(builder.Configuration);

// Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

// Exception Handling
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "KinCare API",
        Version = "v1",
        Description = "Multi-tenant B2B SaaS platform for senior living facility transport coordination"
    });

    // Add JWT authentication to Swagger
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Startup validation — fail fast in production if critical secrets are missing
if (!app.Environment.IsDevelopment())
{
    var twilioConfig = app.Configuration.GetSection("Twilio").Get<TwilioConfig>();
    if (string.IsNullOrWhiteSpace(twilioConfig?.AuthToken))
        throw new InvalidOperationException(
            "CRITICAL: Twilio:AuthToken is not configured. " +
            "Set TWILIO_AUTH_TOKEN environment variable before starting in production.");

    var jwtKey = app.Configuration["Jwt:SecretKey"];
    if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
        throw new InvalidOperationException(
            "CRITICAL: Jwt:SecretKey is missing or too short (minimum 32 characters). " +
            "Set KINCARE_JWT_SECRET_KEY environment variable.");

    var dbConn = app.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(dbConn))
        throw new InvalidOperationException(
            "CRITICAL: ConnectionStrings:DefaultConnection is not configured. " +
            "Set KINCARE_DB_CONNECTION_STRING environment variable.");
}

// Middleware pipeline
app.UseExceptionHandler();
app.UseSerilogRequestLogging(opts =>
{
    opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    opts.EnrichDiagnosticContext = (diag, ctx) =>
    {
        diag.Set("RequestHost", ctx.Request.Host.Value);
        diag.Set("UserAgent", ctx.Request.Headers.UserAgent.ToString());
        if (ctx.User.Identity?.IsAuthenticated == true)
            diag.Set("UserId", ctx.User.FindFirst("sub")?.Value);
    };
});

// Swagger UI (Development only)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "KinCare API v1");
        options.RoutePrefix = "swagger";
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseIpRateLimiting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantMiddleware>();

// Endpoints
app.MapAuthEndpoints();
app.MapOnboardingEndpoints();
app.MapResidentEndpoints();
app.MapVendorEndpoints();
app.MapRideEndpoints();
app.MapDeviceEndpoints();
app.MapTrackingEndpoints();
app.MapHistoryEndpoints();
app.MapOrgAdminEndpoints();
app.MapUserEndpoints();
app.MapBillingEndpoints();

// Health Checks
app.MapHealthChecks("/health");

// Webhooks
app.MapTwilioWebhook();
app.MapBrokerWebhook();
app.MapStripeWebhook();

// SignalR Hubs
app.MapHub<RideStatusHub>("/hubs/ride-status");

// Hangfire Dashboard — dev: local-only; production: SuperAdmin JWT required
if (app.Environment.IsDevelopment())
{
    app.MapHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new Hangfire.Dashboard.LocalRequestsOnlyAuthorizationFilter() }
    });
}
else
{
    app.MapHangfireDashboard("/admin/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAuthorizationFilter() }
    }).RequireAuthorization();
}

// Recurring Jobs - Use injected IRecurringJobManager
using (var scope = app.Services.CreateScope())
{
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobManager.AddOrUpdate<EscalationJob>("escalation-check",
        job => job.ExecuteAsync(), "*/5 * * * *");
    recurringJobManager.AddOrUpdate<ExternalTripSyncJob>("external-trip-sync",
        job => job.ExecuteAsync(), "*/2 * * * *");
}

app.Run();

public partial class Program { }
