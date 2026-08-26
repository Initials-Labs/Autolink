using Microsoft.Extensions.Options;
using OC.AutoLink.Linking;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.PropertyEditors.DeliveryApi;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Cms.Core.Strings;

namespace OC.AutoLink.PropertyEditors;

/// <summary>
/// Wraps Umbraco's built-in rich text value converter and auto-links keywords in whatever markup it produces.
/// </summary>
/// <remarks>
/// <para>
/// This decorates rather than subclasses <see cref="RteBlockRenderingValueConverter"/> deliberately. That type
/// has a sixteen parameter constructor, and ships alongside an already obsolete fourteen parameter overload —
/// its dependency list moves between minor versions. Injecting the converter instead of inheriting from it
/// means none of those parameters are named here, so the wrapper survives that churn.
/// </para>
/// <para>
/// Because this sits at the value converter layer it covers rich text nested inside Block List, Block Grid and
/// nested blocks with no view changes: each of those is an ordinary published property that converts the same
/// way. In the Clean starter kit that is the only path there is — article body copy is entirely
/// <c>richTextRow.content</c> inside a Block List.
/// </para>
/// </remarks>
internal sealed class AutoLinkRichTextValueConverter : IPropertyValueConverter, IDeliveryApiPropertyValueConverter
{
    private readonly RteBlockRenderingValueConverter _inner;
    private readonly IAutoLinker _linker;
    private readonly IOptionsMonitor<AutoLinkOptions> _options;

    public AutoLinkRichTextValueConverter(
        RteBlockRenderingValueConverter inner,
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
    /// Output depends on the whole keyword set, not just this property, so the default <c>Elements</c> level
    /// would serve stale markup after an unrelated page's keywords changed. <c>None</c> keeps the proof of
    /// concept honest — measure before adding a cache layer keyed on the registry stamp.
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

    // The Delivery API path is out of scope for the proof of concept, but it still has to work. Delegating
    // straight through keeps it exactly as it was rather than silently regressing it to unconverted output.
    public PropertyCacheLevel GetDeliveryApiPropertyCacheLevel(IPublishedPropertyType propertyType) =>
        _inner.GetDeliveryApiPropertyCacheLevel(propertyType);

    public PropertyCacheLevel GetDeliveryApiPropertyCacheLevelForExpansion(IPublishedPropertyType propertyType) =>
        _inner.GetDeliveryApiPropertyCacheLevelForExpansion(propertyType);

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
