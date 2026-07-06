module Frank.Cli.Core.Status

open Frank.Semantic.LockFile

let format (lf: LockFile) : string =
    let c = countByStatus lf.Mappings
    $"Confirmed:  {c.Confirmed}\nProposed:   {c.Proposed}\nUnresolved: {c.Unresolved}\nExcluded:   {c.Excluded}"

let private formatGroupBlock (g: PackageGroup) : string =
    let statusLine =
        $"Confirmed:  {g.Counts.Confirmed}\nProposed:   {g.Counts.Proposed}\nUnresolved: {g.Counts.Unresolved}\nExcluded:   {g.Counts.Excluded}"

    if g.Vocabs = [] then
        $"{g.Namespace}\n{statusLine}"
    else
        let vocabList =
            g.Vocabs |> List.map (fun (k, n) -> $"{k} ({n})") |> String.concat ", "

        $"{g.Namespace}\n{statusLine}\nvocabs: {vocabList}"

let formatByPackage (lf: LockFile) : string =
    let groups = countByPackage lf.Mappings
    groups |> List.map formatGroupBlock |> String.concat "\n\n"
