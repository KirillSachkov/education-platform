using AuthService.Domain;
using Microsoft.AspNetCore.Identity;

namespace AuthService.Core.Services;

public sealed class UsernameGenerator(UserManager<Account> userManager)
{
    private const int MAX_RETRIES = 5;
    private const int MAX_USERNAME_LENGTH = 30;
    private const int SUFFIX_LENGTH = 5; // "-XXXX"

    public async Task<string> GenerateUniqueAsync(string baseUsername)
    {
        string truncatedBase = baseUsername.Length > MAX_USERNAME_LENGTH
            ? baseUsername[..MAX_USERNAME_LENGTH]
            : baseUsername;

        string username = truncatedBase;
        for (int i = 0; i < MAX_RETRIES; i++)
        {
            if (await userManager.FindByNameAsync(username) is null)
                return username;

            string shortBase = truncatedBase.Length > MAX_USERNAME_LENGTH - SUFFIX_LENGTH
                ? truncatedBase[..(MAX_USERNAME_LENGTH - SUFFIX_LENGTH)]
                : truncatedBase;
            username = $"{shortBase}-{System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000, 10000)}";
        }

        return username;
    }
}
