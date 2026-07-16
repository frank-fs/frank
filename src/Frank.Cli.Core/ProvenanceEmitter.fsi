module Frank.Cli.Core.ProvenanceEmitter

open Frank.Semantic

/// Emit a GeneratedProvenance F# module from a lock file and vocabulary registry.
val emit: moduleName: string -> registry: VocabularyRegistry -> lock: LockFile.LockFile -> Result<string, string>
