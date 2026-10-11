using System.Net;
using System.Text.Json;
namespace Bike_fleet_operation_platform.Services;

public static class GbfsHttp
{
    public static async Task<JsonDocument> GetJson(HttpClient client, string path, int maxAttempts, CancellationToken token)
    {
        if (maxAttempts is < 1 or > 3) throw new InvalidOperationException("Http:MaxAttempts must be between 1 and 3.");
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(path, token);
                if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < maxAttempts)
                { await Task.Delay(TimeSpan.FromSeconds(attempt), token); continue; }
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                return await JsonDocument.ParseAsync(stream, cancellationToken: token);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && ex.StatusCode is null)
            { await Task.Delay(TimeSpan.FromSeconds(attempt), token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && attempt < maxAttempts)
            { await Task.Delay(TimeSpan.FromSeconds(attempt), token); }
        }
    }
}
