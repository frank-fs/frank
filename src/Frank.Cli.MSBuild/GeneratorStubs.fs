namespace Frank.Cli.MSBuild

open Frank.Semantic

/// Stub generator for Frank.Validation (B11). No-op until B11 is implemented.
type GenerateValidationTask() =
    inherit SemanticGeneratorTask()
    override _.GenerateFiles(_lockFile: LockFile) = ()

/// Stub generator for Frank.LinkedData (B12). No-op until B12 is implemented.
type GenerateLinkedDataTask() =
    inherit SemanticGeneratorTask()
    override _.GenerateFiles(_lockFile: LockFile) = ()

/// Stub generator for Frank.Provenance (B13). No-op until B13 is implemented.
type GenerateProvenanceTask() =
    inherit SemanticGeneratorTask()
    override _.GenerateFiles(_lockFile: LockFile) = ()

/// Stub generator for Frank.Discovery (B14). No-op until B14 is implemented.
type GenerateDiscoveryTask() =
    inherit SemanticGeneratorTask()
    override _.GenerateFiles(_lockFile: LockFile) = ()
