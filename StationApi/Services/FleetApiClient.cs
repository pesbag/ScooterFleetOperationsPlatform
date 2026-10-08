using System.Net;
using System.Text.Json;

namespace StationApi.Services;

public class ApiFailure(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

// Razor controllers use HTTP API contracts, never databases. Cookies are forwarded per request,
// not stored in a shared HttpClient cookie container.
public class FleetApiClient(IHttpClientFactory factory, IHttpContextAccessor accessor)
{
    public async Task<JsonElement> Send(string path, HttpMethod? method = null, object? body = null)
    {
        var context = accessor.HttpContext!;
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        if (context.Request.Headers.TryGetValue("Cookie", out var cookies)) request.Headers.TryAddWithoutValidation("Cookie", cookies.ToString());
        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync();
            request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", form["__RequestVerificationToken"].ToString());
        }
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await factory.CreateClient("FleetApi").SendAsync(request, context.RequestAborted);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            foreach (var cookie in setCookies) context.Response.Headers.Append("Set-Cookie", cookie);
        string json = await response.Content.ReadAsStringAsync(context.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            string message = response.StatusCode switch { HttpStatusCode.Unauthorized => "Please sign in to continue.",
                HttpStatusCode.Forbidden => "You do not have permission for this action.",
                HttpStatusCode.TooManyRequests => "Too many sign-in attempts. Try again in a minute.", _ => "The request could not be completed." };
            try
            {
                using var error = JsonDocument.Parse(json);
                if (error.RootElement.TryGetProperty("message", out var detail)) message = detail.GetString()!;
                else if (error.RootElement.TryGetProperty("errors", out var errors))
                    message = string.Join(" ", errors.EnumerateObject().SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString())));
            }
            catch (JsonException) { }
            throw new ApiFailure(response.StatusCode, message);
        }
        return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<JsonElement>(json);
    }
}
