using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

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
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;

    public AuthController(IConfiguration config) => _config = config;

    public record TokenRequest(string CustomerId);
    public record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);

    /// <summary>Mints a dev-only JWT for the given customerId. No credential check.</summary>
    [HttpPost("token", Name = "IssueDevToken")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public ActionResult<TokenResponse> IssueToken([FromBody] TokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId))
            return Problem(title: "customerId is required.", statusCode: StatusCodes.Status400BadRequest);

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

        return Ok(new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token), expires));
    }
}
