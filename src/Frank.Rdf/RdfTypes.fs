namespace Frank.Rdf

open System

[<Struct>]
[<RequireQualifiedAccess>]
type Node =
    | Iri of string
    | Blank of string

[<RequireQualifiedAccess>]
type Literal =
    | String of string
    | Int of int
    | Bool of bool
    | DateTime of DateTimeOffset
    | LangString of string * string

[<Struct>]
[<RequireQualifiedAccess>]
type Value =
    | Node of node: Node
    | Literal of literal: Literal

type Description =
    { Subject: Node
      Statements: (string * Value) list }

module Node =
    let blank () : Node = Node.Blank(Guid.NewGuid().ToString())

[<AutoOpen>]
module RdfVocabulary =
    let RdfTypeIri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"

[<AutoOpen>]
module Iri =
    let private nonHierarchicalAbsoluteSchemes = [ "urn:"; "mailto:"; "tel:" ]

    let internal resolveIri (prefixes: (string * string) list) (s: string) : string =
        match s.IndexOf ':' with
        | -1 -> failwithf "Frank.Rdf: '%s' is neither an absolute IRI nor a CURIE (no ':')" s
        | i ->
            let prefix = s.Substring(0, i)

            match prefixes |> List.tryFind (fun (p, _) -> p = prefix) with
            | Some(_, ns) -> ns + s.Substring(i + 1)
            | None ->
                let looksAbsolute =
                    s.Substring(i + 1).StartsWith("//")
                    || nonHierarchicalAbsoluteSchemes
                       |> List.exists (fun scheme -> s.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))

                if looksAbsolute && Uri.IsWellFormedUriString(s, UriKind.Absolute) then
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
