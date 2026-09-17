using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NovaWallet.Api.ExceptionHandling;
using NovaWallet.Api.Middleware;
using NovaWallet.Application.Interfaces;
using NovaWallet.Application.Services;
using NovaWallet.Infrastructure;
using NovaWallet.Infrastructure.Persistence;
using NovaWallet.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = jwtSection["SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

// --- Data layer ---
builder.Services.AddDbContext<NovaWalletDbContext>(opt =>
    opt.UseNpgsql(connectionString)
       .AddInterceptors(new AppendOnlyAuditInterceptor()));

builder.Services.AddScoped<IWalletRepository, EfWalletRepository>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();

// --- Application services ---
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

// --- MVC / Swagger ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NovaWallet Ledger Service",
        Version = "v1",
        Description = "Simplified wallet ledger for FirstBank NovaPay's NovaWallet module."
    });

    var bearerScheme = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access_token returned from POST /api/auth/token"
    };
    c.AddSecurityDefinition("Bearer", bearerScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

// --- AuthN / AuthZ ---
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// --- Rate limiting: protect the transfer endpoint from abuse ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("transfer", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirst("customerId")?.Value
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0
            }));
});

// --- Health checks: container-orchestration friendly ---
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres");

// --- RFC 7807 Problem Details, the .NET 8 built-in way ---
// AddProblemDetails() makes [ApiController]'s automatic 400s (invalid model
// state) RFC 7807-shaped too, not just our own thrown exceptions, and lets us
// attach the trace id to every problem response from one place.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

var app = builder.Build();

// Bring the schema up automatically so `docker compose up` is genuinely self-sufficient.
// Trade-off: EnsureCreated (not migrations) for this take-home; see README.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NovaWalletDbContext>();
    db.Database.EnsureCreated();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

app.UseSwagger();
app.UseSwaggerUI();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program { }
