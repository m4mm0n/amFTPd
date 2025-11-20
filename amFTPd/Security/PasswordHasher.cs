/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using System.Security.Cryptography;

namespace amFTPd.Security
{
    /// <summary>
    /// Provides methods for hashing and verifying passwords using the PBKDF2-SHA256 algorithm.
    /// </summary>
    /// <remarks>This class is designed to securely hash passwords and verify them against stored hashes.  It
    /// uses the PBKDF2-SHA256 algorithm with a configurable number of iterations to derive  cryptographic hashes. The
    /// generated hashes include the iteration count, a randomly  generated salt, and the derived hash, all encoded in
    /// Base64. The class also ensures  constant-time comparison during verification to mitigate timing
    /// attacks.</remarks>
    public static class PasswordHasher
    {
        /// <summary>
        /// Hashes a password using the PBKDF2-SHA256 algorithm with a specified number of iterations.
        /// </summary>
        /// <remarks>The generated hash includes the number of iterations, a randomly generated salt, and
        /// the derived hash,  all encoded in Base64. This format allows the hash to be verified later by extracting the
        /// salt and iterations.</remarks>
        /// <param name="password">The password to hash. Cannot be <see langword="null"/>.</param>
        /// <param name="iterations">The number of iterations to use for the key derivation function. Must be a positive integer. The default is
        /// 100,000.</param>
        /// <returns>A string representing the hashed password in the format: 
        /// <c>PBKDF2-SHA256:iterations:saltBase64:hashBase64</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="password"/> is <see langword="null"/>.</exception>
        public static string HashPassword(string password, int iterations = 100_000)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));

            using var rng = RandomNumberGenerator.Create();
            var salt = new byte[16];
            rng.GetBytes(salt);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
            var hash = pbkdf2.GetBytes(32);

            return $"PBKDF2-SHA256:{iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }
        /// <summary>
        /// Verifies whether the provided password matches the stored hashed password.
        /// </summary>
        /// <remarks>This method uses the PBKDF2-SHA256 algorithm to derive a hash from the provided
        /// password and compares it to the expected hash stored in the <paramref name="stored"/> parameter. The
        /// comparison is performed in constant time to prevent timing attacks.</remarks>
        /// <param name="password">The plaintext password to verify. Cannot be <see langword="null"/>.</param>
        /// <param name="stored">The stored password hash in the format "PBKDF2-SHA256:iterations:salt:hash". Must be a non-empty string and
        /// follow the expected format.</param>
        /// <returns><see langword="true"/> if the password matches the stored hash; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="password"/> is <see langword="null"/>.</exception>
        public static bool VerifyPassword(string password, string stored)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));
            if (string.IsNullOrWhiteSpace(stored)) return false;

            var parts = stored.Split(':');
            if (parts.Length != 4) return false;
            if (!parts[0].Equals("PBKDF2-SHA256", StringComparison.Ordinal)) return false;
            if (!int.TryParse(parts[1], out var iterations)) return false;

            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
            var actualHash = pbkdf2.GetBytes(expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
    }
}
