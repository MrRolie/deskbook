using System.Net;
using Deskbook.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Deskbook.IntegrationTests;

/// <summary>The page over a database that has no tables yet, as on the first start of a deployment.</summary>
public class WebWithoutSchemaTests : IClassFixture<TestDatabase>, IAsyncLifetime
{
    private readonly TestDatabase _db;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public WebWithoutSchemaTests(TestDatabase db)
    {
        _db = db;
    }

    public async Task InitializeAsync()
    {
        _app = WebApp.Build(new[] { "--urls", "http://127.0.0.1:0" }, _db.ConnectionString);
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task The_page_says_what_to_do_instead_of_failing()
    {
        var response = await _http.GetAsync("/");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("deskbook db init", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_JSON_endpoint_answers_503_in_JSON()
    {
        var response = await _http.GetAsync("/api/book");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("\"error\": \"unavailable\"", await response.Content.ReadAsStringAsync());
    }
}
