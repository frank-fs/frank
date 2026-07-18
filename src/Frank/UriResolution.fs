namespace Frank

open System

/// RFC 3986 §5.3 relative-reference resolution — the ONE place this rule lives, shared by
/// every caller that must resolve a codegen-emitted, possibly-relative IRI against a live
/// request origin (Frank.LinkedData.Ontology.resolveAbsolute, Frank.Discovery.DiscoveryMiddleware
/// .resolveHref). Both call sites already reference this `Frank` project, so the shared rule
/// lives here rather than creating a new cross-assembly dependency (#398 /simplify item 1).
[<RequireQualifiedAccess>]
module UriResolution =

    /// `u` already absolute (e.g. an external vocab IRI) passes through unchanged — never
    /// rebased against `baseUri`, even when supplied. A relative `u` (the app's own
    /// declared-only, host-relative IRI) is rebased against `baseUri`.
    let resolveAgainst (baseUri: Uri) (u: Uri) : Uri =
        if u.IsAbsoluteUri then u else Uri(baseUri, u)
