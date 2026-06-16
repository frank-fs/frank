module Frank.Cli.Core.Status

open Frank.Semantic.LockFile

let format (lf: LockFile) : string =
    let c = countByStatus lf.Mappings
    $"Confirmed:  {c.Confirmed}\nProposed:   {c.Proposed}\nUnresolved: {c.Unresolved}"
