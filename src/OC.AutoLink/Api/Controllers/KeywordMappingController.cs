using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OC.AutoLink.Api.Models;
using OC.AutoLink.Models;
using OC.AutoLink.Persistence;
using OC.AutoLink.Registry;

namespace OC.AutoLink.Api.Controllers;

/// <summary>
/// The mapping screen's backend. Reads straight off the registry snapshot, so what the screen offers is exactly
/// what the renderer considered — no second query that could disagree with it.
/// </summary>
public sealed class KeywordMappingController : AutoLinkControllerBase
{
    private readonly IKeywordRegistry _registry;
    private readonly IKeywordMappingStore _store;

    public KeywordMappingController(IKeywordRegistry registry, IKeywordMappingStore store)
    {
        _registry = registry;
        _store = store;
    }

    /// <summary>
    /// Every keyword the registry knows about, per culture, contested ones first.
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
        // The same precedence the registry resolves with, so the screen cannot disagree with the renderer.
        Dictionary<string, KeywordMapping> mappings = KeywordMapping.InForce(allMappings, culture);

        var conflicted = new HashSet<string>(
            set.Conflicts.Select(c => c.Keyword),
            StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> keywords = set.Targets.Keys
            .Union(set.Candidates.Keys, StringComparer.OrdinalIgnoreCase);

        List<KeywordRowResponseModel> rows = keywords
            .Select(keyword => BuildRow(keyword, set, mappings, conflicted))
            .OrderByDescending(row => row.HasConflict)
            .ThenBy(row => row.Keyword, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CultureOverviewResponseModel
        {
            Culture = culture,
            Total = rows.Count,
            Conflicts = set.Conflicts.Count,
            Manual = rows.Count(r => r.Source == "manual"),
            Keywords = rows,
        };
    }

    /// <summary>
    /// Pins a keyword to a page. Replaces any existing decision for that keyword.
    /// </summary>
    [HttpPut("mapping")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Save(SaveKeywordMappingRequestModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Keyword))
        {
            return BadRequest("A mapping needs a keyword.");
        }

        bool wantsExternal = !string.IsNullOrWhiteSpace(model.ExternalUrl);

        if (wantsExternal == (model.TargetKey != Guid.Empty))
        {
            return BadRequest("A mapping needs either a page or an external URL, not both and not neither.");
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

        // The stamp only moves if this actually changed where the keyword points, so re-saving the same
        // decision costs a rebuild and nothing else.
        _registry.Invalidate();

        return Ok();
    }

    /// <summary>
    /// Hands a keyword back to automatic resolution. If it is contested, it goes back to being a conflict.
    /// </summary>
    /// <remarks>Idempotent, for the same reason as lifting a suppression.</remarks>
    [HttpDelete("mapping")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Delete([FromQuery] string keyword, [FromQuery] string? culture)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return BadRequest("A mapping needs a keyword.");
        }

        bool removed = _store.Delete(keyword, culture ?? string.Empty);

        if (removed)
        {
            _registry.Invalidate();
        }

        return Ok(new { removed });
    }

    private static KeywordRowResponseModel BuildRow(
        string keyword,
        CultureKeywordSet set,
        Dictionary<string, KeywordMapping> mappings,
        HashSet<string> conflicted)
    {
        set.Targets.TryGetValue(keyword, out KeywordTarget? target);

        IReadOnlyList<KeywordCandidate> candidates =
            set.Candidates.TryGetValue(keyword, out IReadOnlyList<KeywordCandidate>? claimants)
                ? claimants
                : [];

        mappings.TryGetValue(keyword, out KeywordMapping? mapping);

        return new KeywordRowResponseModel
        {
            Keyword = keyword,
            Source = target is null ? "unresolved" : target.Source.ToString().ToLowerInvariant(),
            HasConflict = conflicted.Contains(keyword),
            TargetKey = target?.TargetKey,
            TargetName = target?.TargetName,
            Url = target?.Url,
            UpdateDate = mapping?.UpdateDate,
            UpdatedBy = mapping?.UpdatedBy,
            MappingCulture = mapping?.Culture,
            Candidates = candidates.Select(c => new KeywordCandidateResponseModel
            {
                TargetKey = c.TargetKey,
                TargetName = c.TargetName,
                Url = c.Url,
                IsSelected = target is not null && target.TargetKey == c.TargetKey,
            }),
        };
    }
}
