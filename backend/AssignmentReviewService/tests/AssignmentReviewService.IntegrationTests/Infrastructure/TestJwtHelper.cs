using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AssignmentReviewService.IntegrationTests.Infrastructure;

public static class TestJwtHelper
{
    public const string TEST_ISSUER = "test-issuer";
    public const string TEST_AUDIENCE = "test-audience";

    private const string TEST_SECRET = "test-secret-key-for-integration-tests-minimum-256-bits!!";

    public static SymmetricSecurityKey GetSigningKey() =>
        new(Encoding.UTF8.GetBytes(TEST_SECRET));

    public static string GenerateAdminToken(Guid? userId = null) =>
        GenerateToken(userId ?? Guid.NewGuid(), "platform-admin");

    public static string GenerateToken(Guid userId, params string[] roles)
    {
        List<Claim> claims =
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString()),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Email, "test@example.com"),
        ];

        foreach (string role in roles)
            claims.Add(new Claim("roles", role));

        SigningCredentials credentials = new(GetSigningKey(), SecurityAlgorithms.HmacSha256);

        JwtSecurityToken token = new(
            issuer: TEST_ISSUER,
            audience: TEST_AUDIENCE,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
