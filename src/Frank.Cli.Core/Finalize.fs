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

let private decideCase (c: CaseMapping) : CaseMapping =
    let payload = c.Payload |> List.map decideField

    match c.Status with
    | Confirmed
    | Excluded -> { c with Payload = payload }
    | Proposed
    | Unresolved ->
        { c with
            Status = Excluded
            Payload = payload }

let private decideShape (shape: MappingShape) : MappingShape =
    match shape with
    | MappingShape.Record fs -> MappingShape.Record(List.map decideField fs)
    | MappingShape.Union cases -> MappingShape.Union(List.map decideCase cases)

let private decideMapping (m: Mapping) : Mapping =
    let shape = decideShape m.Shape

    match m.Status with
    | Confirmed
    | Excluded -> { m with Shape = shape }
    | Proposed
    | Unresolved ->
        { m with
            Status = Excluded
            Shape = shape }

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
