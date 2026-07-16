module Frank.Cli.Core.Finalize

open Frank.Semantic

type FinalizeSummary =
    { Confirmed: int
      Excluded: int
      AlreadyDecided: int }

val stampOwnedVocabs:
    appBaseUri: string -> vocabs: Map<string, LockFile.VocabularyEntry> -> Map<string, LockFile.VocabularyEntry>

val run: lf: LockFile.LockFile -> LockFile.LockFile * FinalizeSummary
