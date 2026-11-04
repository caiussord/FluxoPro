using System.Security.Cryptography;
using System.Text;

namespace FluxoPro.Api.Endpoints;

internal static class InternalAuth
{
    public static bool Allowed(HttpRequest request, IConfiguration config)
    {
        var expected = config["FLUXOPRO_INTERNAL_KEY"];
        var actual = request.Headers["X-Internal-Key"].ToString();
        return !string.IsNullOrWhiteSpace(expected) && expected.Length == actual.Length &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
    }
}
