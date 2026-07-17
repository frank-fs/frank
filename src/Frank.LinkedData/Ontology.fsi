module Frank.LinkedData.Ontology

open System
open VDS.RDF
open Frank.Semantic

/// `baseUri`: when Some, rebases any relative (owned, not-yet-resolved) Uri in `ontology`
/// against it — the real deployed origin at call time (#396 round 5). External vocab Uris
/// (already absolute) are always passed through unchanged, regardless of `baseUri`. When None,
/// a relative Uri fails loud with ArgumentException instead of rebasing.
val toGraph: baseUri: Uri option -> ontology: OntologyDecl -> IGraph

/// `baseUri` is accepted for signature parity with toGraph/graphFor but is never used to rebase
/// `ontology.ContextBases` — every entry must already be absolute (ContextBases is built
/// exclusively from `using`, i.e. genuinely external, vocab prefixes) and fails loud with
/// ArgumentException otherwise, regardless of whether `baseUri` is Some or None (#396 round 7).
val toJsonLdContext: baseUri: Uri option -> ontology: OntologyDecl -> string
