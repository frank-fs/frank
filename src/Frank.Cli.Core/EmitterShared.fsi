/// Shared helpers for the code-generation emitters. Not consumed outside this
/// assembly (DiscoveryEmitter/LinkedDataEmitter/ProvenanceEmitter/ValidationEmitter
/// are the sole callers); narrowed to internal (#392).
module internal Frank.Cli.Core.EmitterShared

open System
open Frank.Semantic

val isExternalIri: using: Set<string> -> prefixes: Map<string, Uri> -> iri: Uri -> bool

val computeKnownNamespaces: registry: VocabularyRegistry -> string list

val declaredOnlyBases: lock: LockFile.LockFile -> Set<string>

val hrefFor: bases: Set<string> -> absoluteUri: string -> string
