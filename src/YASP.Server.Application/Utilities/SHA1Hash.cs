using System.Security.Cryptography;
using System.Text;

namespace YASP.Server.Application.Utilities
{
    public static class SHA1Hash
    {
        public static string Hash(string input)
        {
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));

            return string.Concat(hash.Select(b => b.ToString("x2")));
        }
    }
}
