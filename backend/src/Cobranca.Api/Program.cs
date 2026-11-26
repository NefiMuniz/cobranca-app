using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Amazon.DynamoDBv2;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using Cobranca.Api;
using Cobranca.Api.Accounts;
using Cobranca.Api.Auth;
using Cobranca.Api.Billing;
using Cobranca.Api.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

// Utility mode: `dotnet run --project src/Cobranca.Api -- hash-password "my password"`
if (args is ["hash-password", var plainPassword])
{
    Console.WriteLine(PasswordHasher.Hash(plainPassword));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Settings are bound lazily (IOptions) so tests and Lambda environment variables can override them.
builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection(AuthSettings.Section));
builder.Services.Configure<StorageSettings>(builder.Configuration.GetSection(StorageSettings.Section));
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection(AppSettings.Section));
builder.Services.Configure<CorsSettings>(builder.Configuration.GetSection(CorsSettings.Section));
builder.Services.Configure<SecuritySettings>(builder.Configuration.GetSection(SecuritySettings.Section));

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();

// No-op outside Lambda; inside Lambda it swaps Kestrel for the Lambda runtime.
// Function URLs use the same payload format (v2) as API Gateway HTTP APIs.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Clock>();
builder.Services.AddSingleton<TokenService>();

// Storage: in-memory for development/tests, DynamoDB on AWS (same table for billing and accounts).
builder.Services.AddSingleton<InMemoryBillingRepository>();
builder.Services.AddSingleton<InMemoryAccountRepository>();
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddSingleton<IBillingRepository>(sp => UseDynamo(sp)
    ? new DynamoDbBillingRepository(sp.GetRequiredService<IAmazonDynamoDB>(), TableName(sp))
    : sp.GetRequiredService<InMemoryBillingRepository>());
builder.Services.AddSingleton<IAccountRepository>(sp => UseDynamo(sp)
    ? new DynamoDbAccountRepository(sp.GetRequiredService<IAmazonDynamoDB>(), TableName(sp))
    : sp.GetRequiredService<InMemoryAccountRepository>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthSettings>>((options, auth) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = TokenService.CreateValidationParameters(auth.Value);
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AdminEndpoints.AdminPolicy, policy => policy.RequireClaim(Tenancy.RoleClaim, Tenancy.AdminRole));

builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IOptions<CorsSettings>>((options, cors) =>
        options.AddDefaultPolicy(policy =>
        {
            if (cors.Value.AllowedOrigins.Length > 0)
            {
                policy.WithOrigins(cors.Value.AllowedOrigins).AllowAnyHeader().AllowAnyMethod();
            }
        }));

// Throttling. Rejections happen before any database read or password hashing, so abusive traffic
// costs one short Lambda invocation each instead of DynamoDB reads and CPU-heavy PBKDF2 work.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global: generous per signed-in account, tight per IP for anonymous traffic.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var security = context.RequestServices.GetRequiredService<IOptions<SecuritySettings>>().Value;
        return context.User.Identity?.IsAuthenticated == true
            ? FixedWindow($"tenant:{context.User.FindFirst(Tenancy.TenantClaim)?.Value}", security.AuthenticatedRequestsPerMinute, TimeSpan.FromMinutes(1))
            : FixedWindow($"ip:{ClientIp(context)}", security.AnonymousRequestsPerMinute, TimeSpan.FromMinutes(1));
    });

    options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, context =>
    {
        var limit = context.RequestServices.GetRequiredService<IOptions<AuthSettings>>().Value.LoginAttemptsPerMinute;
        return FixedWindow(ClientIp(context), limit, TimeSpan.FromMinutes(1));
    });

    options.AddPolicy(AuthEndpoints.SignupRateLimitPolicy, context =>
    {
        var limit = context.RequestServices.GetRequiredService<IOptions<AuthSettings>>().Value.SignupRequestsPerHour;
        return FixedWindow(ClientIp(context), limit, TimeSpan.FromHours(1));
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Refuse oversized bodies before they are read (the largest legitimate payload is a few hundred bytes).
app.Use(async (context, next) =>
{
    var maxBytes = context.RequestServices.GetRequiredService<IOptions<SecuritySettings>>().Value.MaxRequestBodyBytes;
    if (context.Request.ContentLength > maxBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    await next(context);
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapBillingEndpoints();

app.Run();

static bool UseDynamo(IServiceProvider sp) =>
    sp.GetRequiredService<IOptions<StorageSettings>>().Value.Provider.Equals("DynamoDb", StringComparison.OrdinalIgnoreCase);

static string TableName(IServiceProvider sp) => sp.GetRequiredService<IOptions<StorageSettings>>().Value.TableName;

static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static RateLimitPartition<string> FixedWindow(string key, int permits, TimeSpan window) =>
    RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = Math.Max(1, permits),
        Window = window,
        QueueLimit = 0,
    });

// Exposes the entry point to WebApplicationFactory in the integration tests.
public partial class Program
{
}
