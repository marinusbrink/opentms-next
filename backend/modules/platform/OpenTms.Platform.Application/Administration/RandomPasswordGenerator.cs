using System;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace OpenTms.Platform.Administration;

/* A password that satisfies the configured policy: one character from every required class,
 * the rest random, then shuffled. Used for "Set random password" when creating a user. */
internal static class RandomPasswordGenerator
{
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%&*?-_";

    public static string Generate(PasswordOptions options)
    {
        var length = Math.Max(options.RequiredLength, 16);
        var pool = Lower + Upper + Digits + Symbols;
        var chars = new char[length];
        var index = 0;

        if (options.RequireLowercase) chars[index++] = Pick(Lower);
        if (options.RequireUppercase) chars[index++] = Pick(Upper);
        if (options.RequireDigit) chars[index++] = Pick(Digits);
        if (options.RequireNonAlphanumeric) chars[index++] = Pick(Symbols);
        for (; index < length; index++) chars[index] = Pick(pool);

        // Fisher-Yates with a cryptographic source, so the required classes are not always in front.
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        var password = new string(chars);
        return password.Distinct().Count() >= options.RequiredUniqueChars ? password : Generate(options);
    }

    private static char Pick(string from) => from[RandomNumberGenerator.GetInt32(from.Length)];
}
