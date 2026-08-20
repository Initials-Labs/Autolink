namespace OC.AutoLink.Models;

/// <summary>
/// One page claiming a keyword. Two or more of these for the same keyword is the case this whole mapping
/// layer exists for — before it, the second one was silently dropped.
/// </summary>
/// <param name="TargetKey">Key of the claiming page.</param>
/// <param name="Url">Its resolved relative URL.</param>
/// <param name="TargetName">Its name, for the backoffice list.</param>
public sealed record KeywordCandidate(Guid TargetKey, string Url, string TargetName);
