using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

// ─── SAFETY GUARD ────────────────────────────────────────────────────────────
// auth-stub MUST NOT run outside Development. This prevents it from accidentally
// being deployed to any Azure environment.
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
if (environment != "Development")
{
    Console.Error.WriteLine("FATAL: auth-stub can only run in ASPNETCORE_ENVIRONMENT=Development.");
    Console.Error.WriteLine("       This service must never be deployed to Azure.");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

// ─── Health endpoint (used by make targets + docker-compose) ─────────────────
app.MapHealthChecks("/health");

// ─── Token endpoint ───────────────────────────────────────────────────────────
// POST /token
// Body: { "tenantId": "...", "userId": "...", "role": "..." }
// Returns: { "token": "<signed JWT>", "expiresAt": "<ISO 8601>" }
//
// The issued JWT matches the shape that per-service auth middleware expects:
//   - iss: "crm-auth-stub"
//   - aud: "crm-api"
//   - sub: userId
//   - tid: tenantId (custom claim — same claim name as Entra ID)
//   - roles: [ role ]
//   - exp: now + 8 hours

app.MapPost("/token", ([FromBody] TokenRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.TenantId))
        return Results.BadRequest(new { error = "tenantId is required" });

    if (string.IsNullOrWhiteSpace(request.UserId))
        return Results.BadRequest(new { error = "userId is required" });

    if (string.IsNullOrWhiteSpace(request.Role))
        return Results.BadRequest(new { error = "role is required" });

    if (!ValidRoles.Contains(request.Role))
        return Results.BadRequest(new { error = $"Unknown role: {request.Role}. Valid roles: {string.Join(", ", ValidRoles.All)}" });

    var signingKey = AuthStubConstants.GetSigningKey();
    var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, request.UserId),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim("tid", request.TenantId),
        new Claim("roles", request.Role),
        new Claim(ClaimTypes.Role, request.Role),
    };

    var expiry = DateTime.UtcNow.AddHours(8);

    var token = new JwtSecurityToken(
        issuer: AuthStubConstants.Issuer,
        audience: AuthStubConstants.Audience,
        claims: claims,
        notBefore: DateTime.UtcNow,
        expires: expiry,
        signingCredentials: credentials
    );

    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Ok(new
    {
        token = tokenString,
        expiresAt = expiry.ToString("O"),
        tenantId = request.TenantId,
        userId = request.UserId,
        role = request.Role,
    });
});

// ─── JWKS endpoint ────────────────────────────────────────────────────────────
// Exposes the signing key in JWKS format so services can validate tokens
// without hard-coding the secret (mirrors the Entra ID well-known endpoint shape).
app.MapGet("/.well-known/jwks.json", () =>
{
    var signingKey = AuthStubConstants.GetSigningKey();
    // For HMAC keys we expose the key ID so services can reference it
    var keyId = AuthStubConstants.KeyId;
    return Results.Ok(new
    {
        keys = new[]
        {
            new
            {
                kty = "oct",
                kid = keyId,
                alg = "HS256",
                use = "sig",
                // Base64Url-encode the key bytes
                k = Base64UrlEncoder.Encode(signingKey.Key),
            }
        }
    });
});

// ─── OpenID Connect discovery ─────────────────────────────────────────────────
app.MapGet("/.well-known/openid-configuration", (HttpContext ctx) =>
{
    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    return Results.Ok(new
    {
        issuer = AuthStubConstants.Issuer,
        jwks_uri = $"{baseUrl}/.well-known/jwks.json",
        token_endpoint = $"{baseUrl}/token",
        claims_supported = new[] { "sub", "tid", "roles" },
    });
});

Console.WriteLine("╔══════════════════════════════════════════════════════╗");
Console.WriteLine("║   CRM Platform auth-stub  (LOCAL DEVELOPMENT ONLY)   ║");
Console.WriteLine("╠══════════════════════════════════════════════════════╣");
Console.WriteLine("║  POST /token  → issue dev JWT                        ║");
Console.WriteLine("║  GET  /health → liveness check                       ║");
Console.WriteLine("║  GET  /.well-known/jwks.json → signing key           ║");
Console.WriteLine("║                                                       ║");
Console.WriteLine("║  Use: ./scripts/local/get-dev-token.sh               ║");
Console.WriteLine("║       --tenant TenantA --role SalesRep               ║");
Console.WriteLine("╚══════════════════════════════════════════════════════╝");

app.Run();
return 0;

// ─── Types ───────────────────────────────────────────────────────────────────

record TokenRequest(string TenantId, string UserId, string Role);

static class AuthStubConstants
{
    // Stable dev signing secret — only meaningful on localhost. Never rotate.
    // Services read DEV_AUTH_STUB_SIGNING_KEY from environment; this is the fallback.
    private const string DefaultSigningSecret = "dev-only-hmac-secret-crm-platform-2024-not-for-production";

    public const string Issuer   = "crm-auth-stub";
    public const string Audience = "crm-api";
    public const string KeyId    = "dev-key-1";

    public static SymmetricSecurityKey GetSigningKey()
    {
        var secret = Environment.GetEnvironmentVariable("DEV_AUTH_STUB_SIGNING_KEY")
                     ?? DefaultSigningSecret;
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
        {
            KeyId = KeyId,
        };
    }
}

static class ValidRoles
{
    private static readonly HashSet<string> _roles = new(StringComparer.Ordinal)
    {
        "SalesRep",
        "SalesManager",
        "TenantAdmin",
        "SupportAgent",
        "MarketingUser",
        "AnalystUser",
        "PlatformAdmin",
    };

    public static bool Contains(string role) => _roles.Contains(role);
    public static IEnumerable<string> All => _roles;
}
