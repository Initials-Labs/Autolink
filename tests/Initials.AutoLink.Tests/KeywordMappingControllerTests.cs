using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Initials.AutoLink.Api.Controllers;
using Initials.AutoLink.Api.Models;
using Initials.AutoLink.Persistence;
using Initials.AutoLink.Registry;

namespace Initials.AutoLink.Tests;

/// <summary>
/// The validation at the API boundary: the first editor-typed strings the package puts in an href.
/// </summary>
public class KeywordMappingControllerTests
{
    private static readonly Guid Page = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IKeywordMappingStore _store = Substitute.For<IKeywordMappingStore>();
    private readonly KeywordMappingController _controller;

    public KeywordMappingControllerTests()
    {
        _controller = new KeywordMappingController(Substitute.For<IKeywordRegistry>(), _store)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public void A_page_destination_is_saved()
    {
        IActionResult result = _controller.Save(new SaveKeywordMappingRequestModel
        {
            Keyword = "Umbraco",
            TargetKey = Page,
        });

        Assert.IsType<OkResult>(result);
        _store.Received(1).Save("Umbraco", Arg.Is<KeywordDestination>(d => d.TargetKey == Page && !d.IsExternal), Arg.Any<string?>(), "");
    }

    [Fact]
    public void An_external_destination_is_saved_normalised()
    {
        IActionResult result = _controller.Save(new SaveKeywordMappingRequestModel
        {
            Keyword = "Umbraco",
            ExternalUrl = "  https://umbraco.com  ",
            Label = " Umbraco HQ ",
            Nofollow = false,
            Culture = "en-GB",
        });

        Assert.IsType<OkResult>(result);
        _store.Received(1).Save(
            "Umbraco",
            Arg.Is<KeywordDestination>(d => d.ExternalUrl == "https://umbraco.com" && d.Label == "Umbraco HQ" && d.Nofollow == false),
            Arg.Any<string?>(),
            "en-GB");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_keyword_is_rejected(string keyword)
    {
        IActionResult result = _controller.Save(new SaveKeywordMappingRequestModel { Keyword = keyword, TargetKey = Page });

        Assert.IsType<BadRequestObjectResult>(result);
        _store.DidNotReceiveWithAnyArgs().Save(default!, default!, default, default!);
    }

    [Fact]
    public void A_keyword_longer_than_the_column_is_rejected()
    {
        IActionResult result = _controller.Save(new SaveKeywordMappingRequestModel
        {
            Keyword = new string('k', 256),
            TargetKey = Page,
        });

        Assert.IsType<BadRequestObjectResult>(result);
        _store.DidNotReceiveWithAnyArgs().Save(default!, default!, default, default!);
    }

    [Fact]
    public void A_keyword_with_both_a_page_and_a_url_is_rejected()
    {
        IActionResult result = _controller.Save(new SaveKeywordMappingRequestModel
        {
            Keyword = "Umbraco",
            TargetKey = Page,
            ExternalUrl = "https://umbraco.com",
        });

        Assert.IsType<BadRequestObjectResult>(result);
        _store.DidNotReceiveWithAnyArgs().Save(default!, default!, default, default!);
    }

    [Fact]
    public void A_keyword_with_neither_a_page_nor_a_url_is_rejected()
    {
        IActionResult result = _controller.Save(new SaveKeywordMappingRequestModel { Keyword = "Umbraco" });

        Assert.IsType<BadRequestObjectResult>(result);
        _store.DidNotReceiveWithAnyArgs().Save(default!, default!, default, default!);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData("umbraco.com")]
    public void A_url_that_is_not_absolute_http_is_rejected(string url)
    {
        IActionResult result = _controller.Save(new SaveKeywordMappingRequestModel
        {
            Keyword = "Umbraco",
            ExternalUrl = url,
        });

        Assert.IsType<BadRequestObjectResult>(result);
        _store.DidNotReceiveWithAnyArgs().Save(default!, default!, default, default!);
    }
}
