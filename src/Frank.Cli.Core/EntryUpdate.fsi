/// VocabularyEntry evidence-update helpers shared by Refresh and Validate.
/// Not consumed outside this assembly (Refresh.fs/Validate.fs are the sole callers);
/// narrowed to internal (#392).
module internal Frank.Cli.Core.EntryUpdate

open System
open Frank.Semantic

val updatedEntry:
    now: DateTimeOffset -> ev: RdfConneg.FetchEvidence -> entry: LockFile.VocabularyEntry -> LockFile.VocabularyEntry

val goneEntry:
    now: DateTimeOffset -> reason: string -> status: int -> entry: LockFile.VocabularyEntry -> LockFile.VocabularyEntry

val probeFailedEntry:
    now: DateTimeOffset -> reason: string -> entry: LockFile.VocabularyEntry -> LockFile.VocabularyEntry

val unverifiableEntry:
    now: DateTimeOffset -> reason: string -> entry: LockFile.VocabularyEntry -> LockFile.VocabularyEntry

val unchangedEntry: now: DateTimeOffset -> entry: LockFile.VocabularyEntry -> LockFile.VocabularyEntry

val outcomesExitCode: isDurable: ('o -> bool) -> isTransient: ('o -> bool) -> outcomes: (string * 'o) list -> int
