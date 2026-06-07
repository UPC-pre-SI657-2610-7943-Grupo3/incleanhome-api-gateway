using System.Text;
using InCleanHome.ApiGateway.Configuration;
using InCleanHome.ApiGateway.Discovery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;


//  Bootstrap logger (so failures during startup are also captured)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Information()
    .CreateLogger();

try
{
    Log.Information("Starting InCleanHome API Gateway");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    
    //  Resolve infrastructure settings from environment
    var consulAddress = Environment.GetEnvironmentVariable("CONSUL_HTTP_ADDR")
                        ?? builder.Configuration["Consul:Address"]
                        ?? "http://consul:8500";

    var serviceName = Environment.GetEnvironmentVariable("SERVICE_NAME")
                      ?? builder.Configuration["Service:Name"]
                      ?? "api-gateway";

    var serviceHost = Environment.GetEnvironmentVariable("SERVICE_HOST")
                      ?? serviceName; // in docker-compose the hostname equals the service name

    var servicePort = int.TryParse(Environment.GetEnvironmentVariable("SERVICE_PORT"), out var p)
                      ? p
                      : 8080;

    Log.Information(
        "Identity: name={Name}, host={Host}, port={Port}, consul={Consul}",
        serviceName, serviceHost, servicePort, consulAddress);

    //  Load configuration from Consul KV (fallback: appsettings.json)
    var loadedFromConsul = await ConsulConfigurationLoader.LoadFromConsulAsync(
        builder.Configuration,
        consulAddress,
        serviceName);

    if (!loadedFromConsul)
    {
        Log.Warning("Running with LOCAL configuration (appsettings.json). " +
                    "Verify that Consul is reachable and that the seeder ran.");
    }

    //  CORS (origins come from config)
    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? Array.Empty<string>();

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            if (corsOrigins.Length > 0)
            {
                policy.WithOrigins(corsOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            }
            else
            {
                // Permissive default for dev; production must define origins.
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            }
        });
    });

    //  JWT Bearer authentication
    //  - signing key from env (secret)
    //  - issuer/audience from Consul (non-sensitive)
    var jwtKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY") ?? string.Empty;
    var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "incleanhome";
    var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "incleanhome-api";

    if (!string.IsNullOrWhiteSpace(jwtKey))
    {
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtAudience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ClockSkew = TimeSpan.FromSeconds(60)
                };
            });
        builder.Services.AddAuthorization();
        Log.Information("JWT Bearer authentication enabled (issuer={Issuer}, audience={Audience})",
            jwtIssuer, jwtAudience);
    }
    else
    {
        Log.Warning("JWT_SIGNING_KEY is not set. JWT authentication is DISABLED. " +
                    "All requests will be forwarded without auth checks.");
    }

    //  YARP Reverse Proxy (routes + clusters from Consul)
    builder.Services
        .AddReverseProxy()
        .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

    //  Service Discovery (opt-in via CONSUL_DISCOVERY_ENABLED env var)
    var registrationOptions = new ConsulRegistrationOptions
    {
        ConsulAddress = consulAddress,
        ServiceName = serviceName,
        ServiceId = $"{serviceName}-{Environment.MachineName}",
        Host = serviceHost,
        Port = servicePort,
        Tags = new[] { "gateway", "yarp", "dotnet" },
        HealthCheckUrl = $"http://{serviceHost}:{servicePort}/health"
    };
    builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(registrationOptions));

    builder.Services.AddHttpClient<ConsulServiceRegistration>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
    });
    builder.Services.AddHostedService<ConsulRegistrationHostedService>();

    //  Health checks
    builder.Services.AddHealthChecks();

    //  Build pipeline
    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseCors();

    if (!string.IsNullOrWhiteSpace(jwtKey))
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    // Health endpoint (used by Consul and Docker healthcheck)
    app.MapHealthChecks("/health");

    // Root endpoint - quick "are you alive" inspection
    app.MapGet("/", () => Results.Ok(new
    {
        service = serviceName,
        status = "running",
        configSource = loadedFromConsul ? "consul" : "appsettings.json",
        version = "1.0.0"
    }));

    // The reverse proxy itself
    app.MapReverseProxy();

    Log.Information("InCleanHome API Gateway ready on port {Port}", servicePort);
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "API Gateway terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
