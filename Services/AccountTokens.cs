using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace DepEdDTRSystem.Services;

public static class AccountTokens
{
    public static string Create() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public static class EmployeeAuthDefaults
{
    public const string Scheme = "Employee";
    public const string CookieName = "DepEdDTR.Employee";
    public const string TemporaryPassword = "deped123";
}
