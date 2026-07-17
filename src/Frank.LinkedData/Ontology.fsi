module Frank.LinkedData.Ontology

open System
open VDS.RDF
open Frank.Semantic

/// `baseUri`: when Some, rebases any relative (owned, not-yet-resolved) Uri in `ontology`
/// against it — the real deployed origin at call time (#396 round 5). External vocab Uris
/// (already absolute) are always passed through unchanged, regardless of `baseUri`. When None,
/// a relative Uri fails loud with ArgumentException instead of rebasing.
val toGraph: baseUri: Uri option -> ontology: OntologyDecl -> IGraph

/// See toGraph for `baseUri` semantics.
val toJsonLdContext: baseUri: Uri option -> ontology: OntologyDecl -> string
