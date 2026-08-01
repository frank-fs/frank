namespace Frank.Rdf

open System

[<AutoOpen>]
module Rdf =
    let internal resolveIri (prefixes: (string * string) list) (s: string) : string =
        match s.IndexOf ':' with
        | -1 -> failwithf "Frank.Rdf: '%s' is neither an absolute IRI nor a CURIE (no ':')" s
        | i ->
            let prefix = s.Substring(0, i)

            match prefixes |> List.tryFind (fun (p, _) -> p = prefix) with
            | Some(_, ns) -> ns + s.Substring(i + 1)
            | None ->
                if Uri.IsWellFormedUriString(s, UriKind.Absolute) then
                    s
                else
                    failwithf "Frank.Rdf: undeclared prefix '%s' in '%s'" prefix s

    let internal validatePrefixes (prefixes: (string * string) list) : unit =
        prefixes
        |> List.groupBy fst
        |> List.iter (fun (prefix, entries) ->
            let uris = entries |> List.map snd |> List.distinct

            if uris.Length > 1 then
                failwithf "Frank.Rdf: prefix '%s' declared with conflicting URIs: %s" prefix (String.concat ", " uris))
