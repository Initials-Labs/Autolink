using Microsoft.Extensions.Options;
using Initials.AutoLink.Linking;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;

namespace Initials.AutoLink.PropertyEditors;

/// <summary>
/// Wraps Umbraco's built-in rich text value converter and auto-links keywords in whatever markup it produces.
/// </summary>
internal sealed class AutoLinkRichTextValueConverter : AutoLinkValueConverter<RteBlockRenderingValueConverter>
{
    public AutoLinkRichTextValueConverter(
        RteBlockRenderingValueConverter inner,
        IAutoLinker linker,
        IOptionsMonitor<AutoLinkOptions> options)
        : base(inner, linker, options)
    {
    }
}
