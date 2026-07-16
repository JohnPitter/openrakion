using System.Net;

namespace RakionServer.Admin;

public static class AdminEndpointPolicy
{
    public static Uri Validate(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Admin__Url deve ser uma URL HTTP(S) absoluta.");

        bool loopback = endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(endpoint.Host, out IPAddress? address) && IPAddress.IsLoopback(address);
        if (!loopback && endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Bind externo do Admin exige HTTPS.");
        return endpoint;
    }
}
