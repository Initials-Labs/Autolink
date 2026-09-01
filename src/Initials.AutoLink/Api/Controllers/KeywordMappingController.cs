using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Initials.AutoLink.Api.Models;
using Initials.AutoLink.Models;
using Initials.AutoLink.Persistence;
using Initials.AutoLink.Registry;

namespace Initials.AutoLink.Api.Controllers;

/// <summary>
/// The keywords screen's backend.
/// </summary>
/// <remarks>
/// Rows come from the store and their destinations from the registry snapshot, so what the screen shows is exactly
/// what the renderer resolved — no second lookup that could disagree with it. A row the registry could not resolve
/// is still returned, marked unresolved, because a keyword pointing at a deleted page is the one thing on this
/// screen that needs somebody to do something.
/// </remarks>
public sealed class KeywordMappingController : AutoLinkControllerBase
{
    /// <summary>Matches the column width, so an over-long keyword is a 400 rather than a database error.</summary>
    private const int MaxKeywordLength = 255;

    private readonly IKeywordRegistry _registry;
    private readonly IKeywordMappingStore _store;

    public KeywordMappingController(IKeywordRegistry registry, IKeywordMappingStore store)
    {
        _registry = registry;
        _store = store;
    }

    /// <summary>
    /// Every keyword, per culture.
    /// </summary>
    [HttpGet("keywords")]
    [ProducesResponseType(typeof(KeywordOverviewResponseModel), StatusCodes.Status200OK)]
    public IActionResult Keywords()
    {
        KeywordSnapshot snapshot = _registry.Current;

        IReadOnlyList<KeywordMapping> mappings = _store.GetAll();

        List<CultureOverviewResponseModel> cultures = snapshot.Cultures
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => BuildCulture(pair.Key, pair.Value, mappings))
            .ToList();

        return Ok(new KeywordOverviewResponseModel
        {
            Stamp = snapshot.Stamp,
            Cultures = cultures,
        });
    }

    private static CultureOverviewResponseModel BuildCulture(
        string culture,
        CultureKeywordSet set,
        IReadOnlyList<KeywordMapping> allMappings)
    {
        // The same precedence the registry resolves with, so the screen cannot disagree with the renderer about
        // which row is in force for this language.
        Dictionary<string, KeywordMapping> mappings = KeywordMapping.InForce(allMappings, culture);

        List<KeywordRowResponseModel> rows = mappings.Values
            .Select(mapping => BuildRow(mapping, set))
            .OrderByDescending(row => row.Source == "unresolved")
            .ThenBy(row => row.Keyword, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CultureOverviewResponseModel
        {
            Culture = culture,
            Total = rows.Count,
            Unresolved = rows.Count(row => row.Source == "unresolved"),
            External = rows.Count(row => row.Source == "external"),
            Keywords = rows,
        };
    }

    private static KeywordRowResponseModel BuildRow(KeywordMapping mapping, CultureKeywordSet set)
    {
        set.Targets.TryGetValue(mapping.Keyword, out KeywordTarget? target);

        return new KeywordRowResponseModel
        {
            Keyword = mapping.Keyword,
            Source = target is null ? "unresolved" : target.Source.ToString().ToLowerInvariant(),
            TargetKey = mapping.IsExternal ? null : mapping.TargetKey,
            TargetName = target?.TargetName,
            TargetVariesByCulture = mapping.IsExternal ? null : target?.VariesByCulture,
            Url = target?.Url,
            ExternalUrl = mapping.ExternalUrl,
            Label = mapping.Label,
            Nofollow = mapping.Nofollow,
            UpdateDate = mapping.UpdateDate,
            UpdatedBy = mapping.UpdatedBy,
            MappingCulture = mapping.Culture,
        };
    }

    /// <summary>
    /// Creates a keyword, or points an existing one somewhere else. One row per keyword per culture, so saving the
    /// same keyword again replaces where it goes rather than adding a second destination.
    /// </summary>
    [HttpPut("mapping")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Save(SaveKeywordMappingRequestModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Keyword))
        {
            return BadRequest("A keyword is needed.");
        }

        if (model.Keyword.Trim().Length > MaxKeywordLength)
        {
            return BadRequest($"A keyword cannot be longer than {MaxKeywordLength} characters.");
        }

        bool wantsExternal = !string.IsNullOrWhiteSpace(model.ExternalUrl);

        if (wantsExternal == (model.TargetKey != Guid.Empty))
        {
            return BadRequest("A keyword needs either a page or an external URL, not both and not neither.");
        }

        KeywordDestination destination;

        if (wantsExternal)
        {
            // Validated here and again when the registry builds: an editor-supplied href is the one place this
            // package could emit a hostile scheme.
            if (!ExternalUrl.TryNormalise(model.ExternalUrl, out string? url))
            {
                return BadRequest("An external link must be an absolute http or https URL.");
            }

            destination = KeywordDestination.External(url, model.Label?.Trim(), model.Nofollow);
        }
        else
        {
            destination = KeywordDestination.Page(model.TargetKey);
        }

        _store.Save(model.Keyword, destination, User.Identity?.Name, model.Culture ?? string.Empty);

        return Ok();
    }

    /// <summary>
    /// Removes a keyword. Nothing links it afterwards — there is no second source for it to fall back to.
    /// </summary>
    /// <remarks>Idempotent, for the same reason as lifting a suppression.</remarks>
    [HttpDelete("mapping")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Delete([FromQuery] string keyword, [FromQuery] string? culture)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return BadRequest("A keyword is needed.");
        }

        bool removed = _store.Delete(keyword, culture ?? string.Empty);

        return Ok(new { removed });
    }
}
