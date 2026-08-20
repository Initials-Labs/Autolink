using System.Collections.Generic;

namespace OC.AutoLink.Models;

/// <summary>
/// A keyword claimed by more than one page with no manual mapping to settle it. Which page a phrase should
/// point at is an editorial call, not something the code can infer, so the registry declines to guess and
/// reports it instead.
/// </summary>
/// <param name="Keyword">The contested keyword.</param>
/// <param name="Candidates">Every page claiming it, in stable order.</param>
public sealed record KeywordConflict(string Keyword, IReadOnlyList<KeywordCandidate> Candidates);
