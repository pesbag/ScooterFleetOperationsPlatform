using System.Net;
using BikeFleetOperationPlatform.Services;
public static class HttpChecks
{
    public static async Task Run()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid/") };
        using var data = await GbfsHttp.GetJsonAsync(client, "status", 3, default);
        if (handler.Calls != 2 || data.RootElement.GetProperty("data").GetProperty("stations").GetArrayLength() != 0) throw new Exception("HTTP retry/valid empty failed");
        Console.WriteLine("PASS: Transient HTTP failure retries; valid empty feed remains empty");
        handler.Fail = true;
        try { using var ignored = await GbfsHttp.GetJsonAsync(client, "status", 1, default); throw new Exception("Failure was hidden"); }
        catch (HttpRequestException) { Console.WriteLine("PASS: Provider failure does not fabricate a successful response"); }
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { using var ignored = await GbfsHttp.GetJsonAsync(client, "status", 3, cancel.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Console.WriteLine("PASS: Source fetch respects cancellation"); }
    }
    private class Handler : HttpMessageHandler
    {
        public int Calls; public bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            return Task.FromResult(new HttpResponseMessage(Fail || Calls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent("{\"data\":{\"stations\":[]}}") });
        }
    }
}
