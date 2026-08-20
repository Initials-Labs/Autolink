using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using OC.AutoLink;
using OC.AutoLink.Caching;
using OC.AutoLink.Models;
using OC.AutoLink.Persistence;
using OC.AutoLink.Registry;
using OC.AutoLink.Scanning;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentPublishing;
using Umbraco.Cms.Core.Models.Editors;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Microsoft.Extensions.Options;

namespace Autolink.Demo;

/// <summary>
/// Development-only harness for driving the auto-linker spikes without clicking through the backoffice.
/// Tagging runs through the real content services, so it fires the same ContentPublishedNotification that
/// invalidates the keyword registry in production.
/// </summary>
[ApiController]
[Route("autolink-demo")]
public sealed class AutoLinkDemoController : ControllerBase
{
    private readonly IContentService _contentService;
    private readonly IContentPublishingService _publishingService;
    private readonly IKeywordRegistry _registry;
    private readonly IOptionsMonitor<AutoLinkOptions> _options;
    private readonly IWebHostEnvironment _environment;
    private readonly IKeywordRegistryInvalidator _invalidator;
    private readonly IKeywordMappingStore _mappingStore;
    private readonly IKeywordSuppressionStore _suppressionStore;
    private readonly IAutoLinkScanner _scanner;
    private readonly IContentTypeService _contentTypeService;
    private readonly PropertyEditorCollection _propertyEditors;
    private readonly IDataTypeService _dataTypeService;
    private readonly ILanguageService _languageService;
    private readonly ITagQuery _tagQuery;
    private readonly IPublishedUrlProvider _urlProvider;

    public AutoLinkDemoController(
        IContentService contentService,
        IContentPublishingService publishingService,
        IKeywordRegistry registry,
        IOptionsMonitor<AutoLinkOptions> options,
        IWebHostEnvironment environment,
        ITagQuery tagQuery,
        IPublishedUrlProvider urlProvider,
        IKeywordMappingStore mappingStore,
        IKeywordRegistryInvalidator invalidator,
        IContentTypeService contentTypeService,
        PropertyEditorCollection propertyEditors,
        IDataTypeService dataTypeService,
        IKeywordSuppressionStore suppressionStore,
        IAutoLinkScanner scanner,
        ILanguageService languageService)
    {
        _languageService = languageService;
        _suppressionStore = suppressionStore;
        _scanner = scanner;
        _propertyEditors = propertyEditors;
        _dataTypeService = dataTypeService;
        _mappingStore = mappingStore;
        _invalidator = invalidator;
        _contentTypeService = contentTypeService;
        _tagQuery = tagQuery;
        _urlProvider = urlProvider;
        _contentService = contentService;
        _publishingService = publishingService;
        _registry = registry;
        _options = options;
        _environment = environment;
    }

    /// <summary>Current keyword set and stamp.</summary>
    [HttpGet("status")]
    public IActionResult Status()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        KeywordSnapshot snapshot = _registry.Current;
        return Ok(new
        {
            snapshot.Stamp,
            Cultures = snapshot.Cultures
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new
                {
                    Culture = pair.Key.Length == 0 ? "(invariant)" : pair.Key,
                    Count = pair.Value.Targets.Count,
                    Keywords = pair.Value.Targets.Values
                        .Select(t => new { t.Keyword, t.Url, t.TargetName, Source = t.Source.ToString() })
                        .OrderBy(t => t.Keyword),
                    Conflicts = pair.Value.Conflicts
                        .Select(c => new
                        {
                            c.Keyword,
                            Candidates = c.Candidates.Select(x => new { x.TargetName, x.Url }),
                        })
                        .OrderBy(c => c.Keyword),
                }),
        });
    }

    /// <summary>
    /// What Umbraco itself thinks of the keyword property: whether it counts as a tags property at all, and with
    /// what configuration. This is the check the repository does before writing tag relations, so a null here is
    /// the whole explanation for tags never reaching the tag store.
    /// </summary>
    [HttpGet("tagconfig")]
    public IActionResult TagConfig(int nodeId)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        IContent? content = _contentService.GetById(nodeId);
        if (content is null)
        {
            return NotFound($"No content with id {nodeId}.");
        }

        string keywordAlias = _options.CurrentValue.KeywordsPropertyAlias;
        IProperty? property = content.Properties[keywordAlias];

        if (property is null)
        {
            return Ok(new { Node = content.Name, HasProperty = false });
        }

        TagConfiguration? config = property.GetTagConfiguration(_propertyEditors, _dataTypeService);
        IDataType? dataType = _dataTypeService.GetAsync(property.PropertyType.DataTypeKey).GetAwaiter().GetResult();

        return Ok(new
        {
            Node = content.Name,
            HasProperty = true,
            property.PropertyType.PropertyEditorAlias,
            DataTypeName = dataType?.Name,
            RawConfiguration = dataType?.ConfigurationData,
            TagConfigurationFound = config is not null,
            TagGroup = config?.Group,
            TagStorageType = config?.StorageType.ToString(),
            TagDelimiter = config?.Delimiter,
        });
    }

    /// <summary>
    /// Culture picture: configured languages, and per culture what the tag store holds, what the culture-filtered
    /// tags query returns, and the keyword values on each node. Answers whether tags vary by culture at all.
    /// </summary>
    [HttpGet("cultures")]
    public async Task<IActionResult> Cultures()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        string keywordAlias = _options.CurrentValue.KeywordsPropertyAlias;
        string group = _options.CurrentValue.TagGroup;

        IEnumerable<ILanguage> languages = await _languageService.GetAllAsync();
        var cultures = languages.Select(l => l.IsoCode).ToList();

        var perCulture = cultures.Select(culture => new
        {
            Culture = culture,
            TagsInStore = _tagQuery.GetAllContentTags(group, culture).Select(t => t.Text).ToList(),
            FromQuery = _tagQuery.GetContentByTagGroup(group, culture)
                .Select(content => new
                {
                    content.Id,
                    content.Name,
                    Url = _urlProvider.GetUrl(content, UrlMode.Relative, culture),
                    Keywords = content.Value<IEnumerable<string>>(keywordAlias, culture),
                })
                .ToList(),
        }).ToList();

        // No culture argument at all: what the registry does today.
        var invariant = new
        {
            TagsInStore = _tagQuery.GetAllContentTags(group).Select(t => t.Text).ToList(),
            FromQuery = _tagQuery.GetContentByTagGroup(group).Select(c => c.Name).ToList(),
        };

        return Ok(new
        {
            Languages = languages.Select(l => new { l.IsoCode, l.CultureName, l.IsDefault }),
            PerCulture = perCulture,
            NoCultureArgument = invariant,
        });
    }

    /// <summary>Dry-run scan: which pages would carry auto-links.</summary>
    [HttpGet("scan")]
    public async Task<IActionResult> Scan()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        return Ok(await _scanner.ScanAsync(HttpContext.RequestAborted));
    }

    /// <summary>Suppresses a keyword on a node, or everywhere with nodeId 0.</summary>
    [HttpGet("suppress")]
    public IActionResult Suppress(string keyword, int nodeId = 0, string culture = "")
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        Guid pageKey = KeywordSuppression.Everywhere;

        if (nodeId != 0)
        {
            IContent? content = _contentService.GetById(nodeId);
            if (content is null)
            {
                return NotFound($"No content with id {nodeId}.");
            }

            pageKey = content.Key;
        }

        _suppressionStore.Suppress(keyword, pageKey, "demo harness", culture);
        _invalidator.InvalidateEverywhere();

        return Ok(new
        {
            Keyword = keyword,
            Scope = pageKey == KeywordSuppression.Everywhere ? "everywhere" : $"node {nodeId}",
            Culture = culture.Length == 0 ? "(all)" : culture,
            StampAfter = _registry.Current.Stamp,
        });
    }

    /// <summary>Lifts a suppression.</summary>
    [HttpGet("allow")]
    public IActionResult Allow(string keyword, int nodeId = 0, string culture = "")
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        Guid pageKey = KeywordSuppression.Everywhere;

        if (nodeId != 0)
        {
            IContent? content = _contentService.GetById(nodeId);
            if (content is null)
            {
                return NotFound($"No content with id {nodeId}.");
            }

            pageKey = content.Key;
        }

        bool removed = _suppressionStore.Allow(keyword, pageKey, culture);
        _invalidator.InvalidateEverywhere();

        return Ok(new { Keyword = keyword, Removed = removed, StampAfter = _registry.Current.Stamp });
    }

    /// <summary>Stored suppressions.</summary>
    [HttpGet("suppressions")]
    public IActionResult Suppressions()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        return Ok(_suppressionStore.GetAll());
    }

    /// <summary>Stored manual mappings.</summary>
    [HttpGet("mappings")]
    public IActionResult Mappings()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        return Ok(_mappingStore.GetAll());
    }

    /// <summary>
    /// Pins a keyword to a node, the same way the backoffice screen does. The node does not have to be tagged.
    /// </summary>
    [HttpGet("map")]
    public IActionResult Map(string keyword, int nodeId, string culture = "")
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        IContent? content = _contentService.GetById(nodeId);
        if (content is null)
        {
            return NotFound($"No content with id {nodeId}.");
        }

        _mappingStore.Save(keyword, KeywordDestination.Page(content.Key), "demo harness", culture);
        _invalidator.InvalidateEverywhere();

        _registry.Current.For(culture).Targets.TryGetValue(keyword, out var target);

        return Ok(new
        {
            Keyword = keyword,
            MappedTo = content.Name,
            ResolvedUrl = target?.Url,
            ResolvedSource = target?.Source.ToString(),
            Culture = culture.Length == 0 ? "(all)" : culture,
            StampAfter = _registry.Current.Stamp,
        });
    }

    /// <summary>Points a keyword at an external URL, the way the dashboard form does.</summary>
    [HttpGet("map-external")]
    public IActionResult MapExternal(string keyword, string url, string culture = "", string? label = null, bool? nofollow = null)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        if (!ExternalUrl.TryNormalise(url, out string? normalised))
        {
            return BadRequest("An external link must be an absolute http or https URL.");
        }

        _mappingStore.Save(
            keyword,
            KeywordDestination.External(normalised, label, nofollow),
            "demo harness",
            culture);

        _invalidator.InvalidateEverywhere();
        _registry.Current.For(culture).Targets.TryGetValue(keyword, out var target);

        return Ok(new
        {
            Keyword = keyword,
            ResolvedUrl = target?.Url,
            ResolvedSource = target?.Source.ToString(),
            Rel = target?.Rel,
            Culture = culture.Length == 0 ? "(all)" : culture,
            StampAfter = _registry.Current.Stamp,
        });
    }

    /// <summary>Hands a keyword back to automatic resolution.</summary>
    [HttpGet("unmap")]
    public IActionResult Unmap(string keyword, string culture = "")
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        bool removed = _mappingStore.Delete(keyword, culture);
        _invalidator.InvalidateEverywhere();

        _registry.Current.For(culture).Targets.TryGetValue(keyword, out var target);

        return Ok(new
        {
            Keyword = keyword,
            Removed = removed,
            ResolvedUrl = target?.Url,
            ResolvedSource = target?.Source.ToString(),
            StampAfter = _registry.Current.Stamp,
        });
    }

    /// <summary>
    /// Raw view of what the tags query hands the registry, for when the registry and the content tree disagree.
    /// </summary>
    [HttpGet("tags")]
    public IActionResult Tags(string? group = null)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        string keywordAlias = _options.CurrentValue.KeywordsPropertyAlias;

        // An explicit empty group means every group, which answers whether tag persistence works at all on this
        // install or only fails for our datatype.
        bool allGroups = group is not null && group.Length == 0;
        group = allGroups ? null! : group ?? _options.CurrentValue.TagGroup;

        var fromQuery = _tagQuery.GetContentByTagGroup(group)
            .Select(content => new
            {
                content.Id,
                content.Name,
                Url = _urlProvider.GetUrl(content, UrlMode.Relative),
                Typed = content.Value<IEnumerable<string>>(keywordAlias),
                Raw = content.Value<object>(keywordAlias)?.ToString(),
            })
            .ToList();

        var allTags = _tagQuery.GetAllContentTags(group)
            .Select(tag => new { tag.Text, tag.Group, tag.NodeCount })
            .ToList();

        return Ok(new { Group = group ?? "(all)", FromQuery = fromQuery, AllTags = allTags });
    }

    /// <summary>
    /// Marks the registry stale without reading it back, so the rebuild happens on the next request rather
    /// than inside this one. Reading the stamp inline is what makes a publish look like it lost keywords.
    /// </summary>
    [HttpGet("invalidate")]
    public IActionResult InvalidateRegistry()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        _invalidator.InvalidateEverywhere();
        return Ok(new { Invalidated = true });
    }

    /// <summary>
    /// Every node that can carry keywords, with its id. Saves guessing node ids when setting up a collision.
    /// </summary>
    [HttpGet("nodes")]
    public IActionResult Nodes()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        string keywordAlias = _options.CurrentValue.KeywordsPropertyAlias;

        var rows = _contentService.GetRootContent()
            .SelectMany(root => _contentService
                .GetPagedDescendants(root.Id, 0, 1000, out _)
                .Prepend(root))
            .Where(content => content.HasProperty(keywordAlias))
            .Select(content => new
            {
                content.Id,
                content.Name,
                Type = content.ContentType.Alias,
                content.Published,
                Keywords = content.GetValue<string>(keywordAlias),
            })
            .ToList();

        return Ok(rows);
    }

    /// <summary>Sets the keyword tags on a node and publishes it.</summary>
    /// <example>/autolink-demo/tag?nodeId=1138&amp;keywords=content editor</example>
    [HttpGet("tag")]
    public async Task<IActionResult> Tag(int nodeId, string keywords)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        IContent? content = _contentService.GetById(nodeId);
        if (content is null)
        {
            return NotFound($"No content with id {nodeId}.");
        }

        string keywordAlias = _options.CurrentValue.KeywordsPropertyAlias;
        if (!content.HasProperty(keywordAlias))
        {
            return BadRequest(
                $"'{content.Name}' is a '{content.ContentType.Alias}' document, which has no '{keywordAlias}' property. " +
                "Add that document type to OC:AutoLink:InstallOnDocumentTypes.");
        }

        string[] values = keywords
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        // The Tags datatype is configured for JSON storage.
        content.SetValue(keywordAlias, JsonSerializer.Serialize(values));
        _contentService.Save(content);

        Attempt<ContentPublishingResult, ContentPublishingOperationStatus> result =
            await _publishingService.PublishAsync(
                content.Key,
                [new CulturePublishScheduleModel { Culture = "*" }],
                Constants.Security.SuperUserKey);

        return Ok(new
        {
            Node = content.Name,
            NodeId = nodeId,
            Keywords = values,
            Published = result.Success,
            Status = result.Status.ToString(),
            StampAfter = _registry.Current.Stamp,
        });
    }

    /// <summary>Clears the keyword tags on a node and republishes it.</summary>
    [HttpGet("untag")]
    public Task<IActionResult> Untag(int nodeId) => Tag(nodeId, string.Empty);

    /// <summary>
    /// Unpublishes a target node. Proves that links to it vanish on the next render of pages that mention it,
    /// rather than leaving dead anchors behind in stored markup.
    /// </summary>
    [HttpGet("unpublish")]
    public async Task<IActionResult> Unpublish(int nodeId)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        IContent? content = _contentService.GetById(nodeId);
        if (content is null)
        {
            return NotFound($"No content with id {nodeId}.");
        }

        // IContentPublishingService.UnpublishAsync wants an explicit culture set, which an invariant site
        // has nothing sensible to put in. IContentService.Unpublish handles invariant directly.
        PublishResult result = _contentService.Unpublish(content, userId: Constants.Security.SuperUserId);

        return Ok(new
        {
            Node = content.Name,
            NodeId = nodeId,
            Unpublished = result.Success,
            Status = result.Result.ToString(),
            StampAfter = _registry.Current.Stamp,
        });
    }

    /// <summary>Republishes a node without changing its keywords.</summary>
    [HttpGet("republish")]
    public async Task<IActionResult> Republish(int nodeId)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        IContent? content = _contentService.GetById(nodeId);
        if (content is null)
        {
            return NotFound($"No content with id {nodeId}.");
        }

        Attempt<ContentPublishingResult, ContentPublishingOperationStatus> result =
            await _publishingService.PublishAsync(
                content.Key,
                [new CulturePublishScheduleModel { Culture = "*" }],
                Constants.Security.SuperUserKey);

        return Ok(new
        {
            Node = content.Name,
            NodeId = nodeId,
            Published = result.Success,
            Status = result.Status.ToString(),
            StampAfter = _registry.Current.Stamp,
        });
    }
}
