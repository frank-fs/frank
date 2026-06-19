module Frank.Cli.Core.Finalize

open Frank.Semantic
open Frank.Semantic.LockFile

type FinalizeSummary =
    { Confirmed: int
      Excluded: int
      AlreadyDecided: int }

let private decideField (f: FieldMapping) : FieldMapping =
    match f.Status with
    | Confirmed
    | Excluded -> f
    | Proposed
    | Unresolved -> { f with Status = Excluded }

let private decideMapping (m: Mapping) : Mapping =
    let fields = m.Fields |> List.map decideField

    match m.Status with
    | Confirmed
    | Excluded -> { m with Fields = fields }
    | Proposed
    | Unresolved ->
        { m with
            Status = Excluded
            Fields = fields }

/// Resolve a draft lock to all-decided: Confirmed stays; everything else Excluded.
/// Deterministic, zero tokens. Pure.
let run (lf: LockFile) : LockFile * FinalizeSummary =
    let decided = lf.Mappings |> List.map decideMapping
    let counts = countByStatus decided

    // count of mappings already decided BEFORE this finalize (over the input lock, not the result)
    let alreadyDecided =
        lf.Mappings |> List.filter (fun m -> isDecided m.Status) |> List.length

    { lf with Mappings = decided },
    { Confirmed = counts.Confirmed
      Excluded = counts.Excluded
      AlreadyDecided = alreadyDecided }
