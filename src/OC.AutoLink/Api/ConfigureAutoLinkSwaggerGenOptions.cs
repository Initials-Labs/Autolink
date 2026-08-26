using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
// Microsoft.OpenApi 2.x, which 17.6.1 ships: OpenApiInfo is no longer under .Models.
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OC.AutoLink.Api;

/// <summary>
/// Registers the Swagger document the controllers map to. Without this the endpoints still route, they just do
/// not show up in the API browser, which makes them harder to poke at from curl during a demo.
/// </summary>
internal sealed class ConfigureAutoLinkSwaggerGenOptions : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options) =>
        options.SwaggerDoc(
            AutoLinkApiConfiguration.ApiName,
            new OpenApiInfo
            {
                Title = AutoLinkApiConfiguration.ApiTitle,
                Version = "1.0",
            });
}
