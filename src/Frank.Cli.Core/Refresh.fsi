module Frank.Cli.Core.Refresh

open System
open Frank.Semantic

type EntryOutcome =
    | DriftDetected of reason: string
    | ProbeFailed of reason: string
    | EvidenceRefreshed
    | SkippedFresh

type RefreshReport =
    { Outcomes: (string * EntryOutcome) list }

val refreshExitCode: report: RefreshReport -> int

val refresh:
    fetch: ConnegFetch ->
    policy: SlaPolicy ->
    now: DateTimeOffset ->
    force: bool ->
    lf: LockFile.LockFile ->
        Async<RefreshReport * LockFile.LockFile>
