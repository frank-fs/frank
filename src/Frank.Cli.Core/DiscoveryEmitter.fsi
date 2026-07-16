module Frank.Cli.Core.DiscoveryEmitter

open Frank.Semantic

type internal ResolvedDescriptor =
    { Id: string
      Href: string option
      IsAction: bool
      Rt: string option
      ClassIri: string option
      RequestClrTypeName: string option
      Children: ResolvedDescriptor list }

/// Pure projection: model → (descriptors, describedBy links). Testable typed output.
/// declaredOnlyBases: set of base URI strings whose IRIs should be emitted as host-relative paths.
val internal projectDiscovery:
    declaredOnlyBases: Set<string> -> model: ResolvedModel -> ResolvedDescriptor list * string list

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
