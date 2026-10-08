var port = args.Length > 0 ? args[0] : "8080";
var path = args.Length > 1 ? args[1] : "/health/live";
try
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var response = await client.GetAsync($"http://localhost:{port}{path}");
    return response.IsSuccessStatusCode ? 0 : 1;
}
catch
{
    return 1;
}
