using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using NovaWallet.Api.Contracts;

namespace NovaWallet.Api.Controllers;

/// <summary>
/// DEV-ONLY mock identity provider. Mints a JWT for whatever customerId is
/// supplied, with no credential verification at all. This exists purely so
/// the panel can exercise the real JWT-bearer middleware and claims handling
/// on the wallet endpoints without us having to stand up a full auth server.
/// A real deployment would delete this controller and point ValidIssuer /
/// signing keys at FirstBank's actual identity provider.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json", "application/problem+json")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;

    public AuthController(IConfiguration config) => _config = config;

    public record TokenRequest(string CustomerId);
    public record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);

    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public ActionResult<ApiResponse<TokenResponse>> IssueToken([FromBody] TokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId))
            return Problem(title: "Invalid request", detail: "customerId is required.", statusCode: StatusCodes.Status400BadRequest);

        var jwtSection = _config.GetSection("Jwt");
        var signingKey = jwtSection["SigningKey"]!;
        var expires = DateTime.UtcNow.AddHours(1);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, request.CustomerId),
            new Claim("customerId", request.CustomerId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return Ok(this.ToApiResponse(new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token), expires)));
    }
}
