using Microsoft.Extensions.Options;
using Initials.AutoLink.Linking;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.PropertyEditors.DeliveryApi;
using Umbraco.Cms.Core.Strings;

namespace Initials.AutoLink.PropertyEditors;

/// <summary>
/// Wraps one of Umbraco's built-in value converters and auto-links keywords in the HTML it produces. Everything
/// delegates to the inner converter; the one addition is running the linker over
/// <see cref="ConvertIntermediateToObject"/>'s markup.
/// </summary>
internal abstract class AutoLinkValueConverter<TInner> : IPropertyValueConverter, IDeliveryApiPropertyValueConverter
    where TInner : class, IPropertyValueConverter, IDeliveryApiPropertyValueConverter
{
    private readonly TInner _inner;
    private readonly IAutoLinker _linker;
    private readonly IOptionsMonitor<AutoLinkOptions> _options;

    protected AutoLinkValueConverter(TInner inner, IAutoLinker linker, IOptionsMonitor<AutoLinkOptions> options)
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
    /// would serve stale markup after an unrelated page's keywords changed.
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

        return ReferenceEquals(linked, markup) ? value : new HtmlEncodedString(linked);
    }

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
