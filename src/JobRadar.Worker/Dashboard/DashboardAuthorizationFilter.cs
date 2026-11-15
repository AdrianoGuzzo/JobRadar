using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hangfire.Dashboard;

namespace JobRadar.Worker.Dashboard;

/// <summary>Sem senha configurada, permite somente acesso local; na rede exige Basic Auth.</summary>
public sealed class DashboardAuthorizationFilter(IConfiguration configuration) : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => Authorize(context.GetHttpContext());

    public bool Authorize(HttpContext context)
    {
        var password = configuration["Dashboard:Password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            var address = context.Connection.RemoteIpAddress;
            return address is not null && IPAddress.IsLoopback(address);
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var credentials = Convert.FromBase64String(header[6..].Trim());
                var expected = Encoding.UTF8.GetBytes($"{configuration["Dashboard:Username"] ?? "admin"}:{password}");
                if (CryptographicOperations.FixedTimeEquals(SHA256.HashData(credentials), SHA256.HashData(expected)))
                {
                    return true;
                }
            }
            catch (FormatException)
            {
                // Credenciais malformadas recebem o mesmo desafio de autenticação.
            }
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"JobRadar\", charset=\"UTF-8\"";
        return false;
    }
}
