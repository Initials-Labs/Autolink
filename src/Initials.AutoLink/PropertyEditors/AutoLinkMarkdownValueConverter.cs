using Microsoft.Extensions.Options;
using Initials.AutoLink.Linking;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;

namespace Initials.AutoLink.PropertyEditors;

/// <summary>
/// Wraps Umbraco's built-in Markdown editor value converter and auto-links keywords in the HTML it produces.
/// </summary>
internal sealed class AutoLinkMarkdownValueConverter : AutoLinkValueConverter<MarkdownEditorValueConverter>
{
    public AutoLinkMarkdownValueConverter(
        MarkdownEditorValueConverter inner,
        IAutoLinker linker,
        IOptionsMonitor<AutoLinkOptions> options)
        : base(inner, linker, options)
    {
    }
}
