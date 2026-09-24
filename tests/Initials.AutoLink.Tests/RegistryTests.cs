using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Initials.AutoLink.Persistence;
using Initials.AutoLink.Registry;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Initials.AutoLink.Tests;

/// <summary>
/// What a rebuild of the real registry hands to the renderer after a keyword row changes.
/// </summary>
/// <remarks>
/// External rows only, so resolution needs no URL provider or content service behind it.
/// </remarks>
public class RegistryTests
{
    private const string Keyword = "Initials CX";
    private const string Url = "https://initials.co.uk";

    private readonly IKeywordMappingStore _mappings = Substitute.For<IKeywordMappingStore>();
    private readonly KeywordRegistry _registry;

    public RegistryTests()
    {
        var suppressions = Substitute.For<IKeywordSuppressionStore>();
        suppressions.GetAll().Returns([]);

        var languages = Substitute.For<ILanguageService>();
        languages.GetAllAsync().Returns(Task.FromResult<IEnumerable<ILanguage>>([]));

        ServiceProvider services = new ServiceCollection()
            .AddSingleton(_mappings)
            .AddSingleton(suppressions)
            .AddSingleton(languages)
            .AddSingleton(Substitute.For<IUmbracoContextFactory>())
            .AddSingleton(Substitute.For<IPublishedUrlProvider>())
            .AddSingleton(Substitute.For<IContentService>())
            .BuildServiceProvider();

        var options = Substitute.For<IOptionsMonitor<AutoLinkOptions>>();
        options.CurrentValue.Returns(new AutoLinkOptions());

        _registry = new KeywordRegistry(
            services.GetRequiredService<IServiceScopeFactory>(),
            options,
            NullLogger<KeywordRegistry>.Instance);
    }

    private void Stored(string? label = null, bool? nofollow = null) =>
        _mappings.GetAll().Returns([new KeywordMapping(Keyword, Guid.Empty, Url, label, nofollow, DateTime.UtcNow, "test", "")]);

    private Models.KeywordTarget Target() => _registry.Current.For(null).Targets[Keyword];

    [Fact]
    public void A_nofollow_change_reaches_the_renderer()
    {
        Stored(nofollow: null);
        Assert.Equal("nofollow", Target().Rel);

        Stored(nofollow: false);
        _registry.Invalidate();

        Assert.Null(Target().Rel);
    }

    [Fact]
    public void A_label_change_reaches_the_renderer()
    {
        Stored(label: "Initials");
        Assert.Equal("Initials", Target().TargetName);

        Stored(label: "Initials CX Ltd");
        _registry.Invalidate();

        Assert.Equal("Initials CX Ltd", Target().TargetName);
    }

    [Fact]
    public void An_unchanged_rebuild_keeps_the_same_stamp()
    {
        Stored();
        string before = _registry.Current.Stamp;

        _registry.Invalidate();

        Assert.Equal(before, _registry.Current.Stamp);
    }
}
