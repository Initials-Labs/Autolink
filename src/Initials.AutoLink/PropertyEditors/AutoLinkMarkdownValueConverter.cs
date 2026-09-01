using Microsoft.Extensions.Options;
using Initials.AutoLink.Linking;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.PropertyEditors.DeliveryApi;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Cms.Core.Strings;

namespace Initials.AutoLink.PropertyEditors;

/// <summary>
/// Wraps Umbraco's built-in Markdown editor value converter and auto-links keywords in the HTML it produces.
/// </summary>
/// <remarks>
/// <para>
/// Same decorate-don't-subclass pattern as <see cref="AutoLinkRichTextValueConverter"/>, for the same reason:
/// none of the inner converter's constructor parameters are named here, so its dependency list can churn
/// between Umbraco versions without touching this type.
/// </para>
/// <para>
/// The linking happens after markdown becomes HTML, which is the only place it can: the linker is an HTML
/// text-node walk, and running it over raw markdown would corrupt link syntax and code fences. It also means
/// hand-written markdown links come out as real anchors before the linker looks, so "skip text already inside
/// an anchor" keeps working unchanged.
/// </para>
/// </remarks>
internal sealed class AutoLinkMarkdownValueConverter : IPropertyValueConverter, IDeliveryApiPropertyValueConverter
{
    private readonly MarkdownEditorValueConverter _inner;
    private readonly IAutoLinker _linker;
    private readonly IOptionsMonitor<AutoLinkOptions> _options;

    public AutoLinkMarkdownValueConverter(
        MarkdownEditorValueConverter inner,
        IAutoLinker linker,
        IOptionsMonitor<AutoLinkOptions> options)
    {
        _inner = inner;
        _linker = linker;
        _options = options;
    }

    public bool IsConverter(IPublishedPropertyType propertyType) => _inner.IsConverter(propertyType);

    public bool? IsValue(object? value, PropertyValueLevel level) => _inner.IsValue(value, level);

    public Type GetPropertyValueType(IPublishedPropertyType propertyType) => _inner.GetPropertyValueType(propertyType);

    /// <summary>
    /// Output depends on the whole keyword set, not just this property, so the inner converter's default level
    /// would serve stale markup after an unrelated page's keywords changed. Same reasoning as the rich text
    /// wrapper, measured there at ~1ms per request.
    /// </summary>
    public PropertyCacheLevel GetPropertyCacheLevel(IPublishedPropertyType propertyType) =>
        _options.CurrentValue.Enabled
            ? PropertyCacheLevel.None
            : _inner.GetPropertyCacheLevel(propertyType);

    public object? ConvertSourceToIntermediate(
        IPublishedElement owner,
        IPublishedPropertyType propertyType,
        object? source,
        bool preview) =>
        _inner.ConvertSourceToIntermediate(owner, propertyType, source, preview);

    public object? ConvertIntermediateToObject(
        IPublishedElement owner,
        IPublishedPropertyType propertyType,
        PropertyCacheLevel referenceCacheLevel,
        object? inter,
        bool preview)
    {
        object? value = _inner.ConvertIntermediateToObject(owner, propertyType, referenceCacheLevel, inter, preview);

        if (!_options.CurrentValue.Enabled || value is not IHtmlEncodedString encoded)
        {
            return value;
        }

        string? markup = encoded.ToHtmlString();
        if (string.IsNullOrWhiteSpace(markup))
        {
            return value;
        }

        string linked = _linker.ProcessMarkup(markup);

        // ProcessMarkup returns the same instance when it changed nothing.
        return ReferenceEquals(linked, markup) ? value : new HtmlEncodedString(linked);
    }

    // The Delivery API gets the raw markdown string from the inner converter, not HTML, so there is nothing an
    // HTML linker could safely do with it. Delegating straight through keeps it exactly as it was.
    public PropertyCacheLevel GetDeliveryApiPropertyCacheLevel(IPublishedPropertyType propertyType) =>
        _inner.GetDeliveryApiPropertyCacheLevel(propertyType);

    // Cast because the inner converter takes this member's default interface implementation rather than
    // declaring it, so it only exists on the interface.
    public PropertyCacheLevel GetDeliveryApiPropertyCacheLevelForExpansion(IPublishedPropertyType propertyType) =>
        ((IDeliveryApiPropertyValueConverter)_inner).GetDeliveryApiPropertyCacheLevelForExpansion(propertyType);

    public Type GetDeliveryApiPropertyValueType(IPublishedPropertyType propertyType) =>
        _inner.GetDeliveryApiPropertyValueType(propertyType);

    public object? ConvertIntermediateToDeliveryApiObject(
        IPublishedElement owner,
        IPublishedPropertyType propertyType,
        PropertyCacheLevel referenceCacheLevel,
        object? inter,
        bool preview,
        bool expanding) =>
        _inner.ConvertIntermediateToDeliveryApiObject(
            owner, propertyType, referenceCacheLevel, inter, preview, expanding);
}
