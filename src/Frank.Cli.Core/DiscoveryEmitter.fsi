module Frank.Cli.Core.DiscoveryEmitter

open Frank.Semantic

type internal ResolvedDescriptor =
    { Id: string
      Href: string option
      Rt: string option
      ClassIri: string option
      RequestClrTypeName: string option
      Children: ResolvedDescriptor list }

/// Pure projection: model → (descriptors, describedBy links). Testable typed output.
/// Each describedBy link is (classIri, formatted `<href>; rel="type"` value) — classIri is
/// DiscoveryMiddleware's correlation key for scoping the link to its matched resource (#398).
/// declaredOnlyBases: set of base URI strings whose IRIs should be emitted as host-relative paths.
val internal projectDiscovery:
    declaredOnlyBases: Set<string> -> model: ResolvedModel -> ResolvedDescriptor list * (string * string) list

/// Codegen-time ALPS Type fallback for any descriptor — always "semantic", deliberately
/// independent of Rt (#400 Fix 2: Rt is a genuine ALPS return-type link, never an
/// HTTP-safety-classification signal). DiscoveryMiddleware reconciles the genuine Type
/// against the resource's actual registered HTTP method(s) at serve time (#397).
val internal alpsTypeDefault: d: ResolvedDescriptor -> string

/// Emit a GeneratedDiscovery F# module from a lock file and vocabulary registry.
///
/// moduleName   — the F# module name to emit (e.g. "TicTacToe.GeneratedDiscovery")
/// profileUri   — the ALPS profile route (e.g. "/alps/tictactoe")
/// registry     — the VocabularyRegistry providing prefix→URI mappings
/// lock         — the resolved lock file
///
/// Returns Ok with the F# source string, or Error with a message if any IRI
/// references an unknown prefix.
val emit:
    moduleName: string ->
    profileUri: string ->
    registry: VocabularyRegistry ->
    lock: LockFile.LockFile ->
        Result<string, string>
