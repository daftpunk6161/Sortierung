using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Romulus.Api;
using Xunit;

namespace Romulus.Tests;

internal static class OpenApiTestHelper
{
    private const string ApiKey = "openapi-test-key";

    public static async Task<string> FetchOpenApiJsonAsync()
    {
        using var factory = CreateFactory();
        using var client = CreateClientWithApiKey(factory);

        var response = await client.GetAsync("/openapi");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync();
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return ApiTestFactory.Create(new Dictionary<string, string?>
        {
            ["ApiKey"] = ApiKey
        });
    }

    private static HttpClient CreateClientWithApiKey(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
