module Frank.Cli.Core.LinkedDataEmitter

open System
open Frank.Semantic
open Frank.Semantic.LockFile

// ── Prefix resolution ────────────────────────────────────────────────────────

/// Resolve the external base IRIs for the @context from the model's Using set and Prefixes map.
/// Iterates Set.toList (ascending) — identical order to the old buildContext loop.
/// Returns Error if any using prefix is not in Prefixes.
let private contextBases (model: ResolvedModel) : Result<Uri list, string> =
    let rec loop (remaining: string list) (acc: Uri list) =
        match remaining with
        | [] -> Ok(List.rev acc)
        | prefix :: rest ->
            match Map.tryFind prefix model.Prefixes with
            | None -> Error $"using prefix '{prefix}' not found in Prefixes"
            | Some baseUri -> loop rest (baseUri :: acc)

    loop (Set.toList model.Using) []

// ── OntologyDecl projection ──────────────────────────────────────────────────

let private toClassDecl (r: ResolvedResource) : ClassDecl option =
    r.ClassIri
    |> Option.map (fun classUri ->
        let props =
            r.Fields
            |> List.choose (fun f -> f.Iri |> Option.map (fun iri -> { Iri = iri; Domain = classUri }))

        { Iri = classUri
          EquivalentClass = r.EquivalentClass
          SeeAlso = r.SeeAlso
          Properties = props })

/// Project a ResolvedModel to an OntologyDecl.
/// ContextBases is left empty; emit fills it after resolving prefix URIs.
let internal projectOntology (model: ResolvedModel) : OntologyDecl =
    { Classes = model.Resources |> List.choose toClassDecl
      ContextBases = [] }

// ── AstRender helpers ────────────────────────────────────────────────────────

let private uriField (name: string) (u: Uri) =
    name, AstRender.appExpr "System.Uri" (AstRender.strExpr u.AbsoluteUri)

let private optUriField (name: string) (u: Uri option) =
    let expr =
        match u with
        | Some v ->
            AstRender.appExpr
                "Some"
                (AstRender.parenExpr (AstRender.appExpr "System.Uri" (AstRender.strExpr v.AbsoluteUri)))
        | None -> AstRender.noneExpr

    name, expr

let private uriListField (name: string) (us: Uri list) =
    let items =
        us
        |> List.map (fun u -> AstRender.appExpr "System.Uri" (AstRender.strExpr u.AbsoluteUri))

    name, AstRender.listExpr items

let private propExpr (p: PropertyDecl) =
    AstRender.recordExpr [ uriField "Iri" p.Iri; uriField "Domain" p.Domain ]

let private classExpr (c: ClassDecl) =
    AstRender.recordExpr
        [ uriField "Iri" c.Iri
          optUriField "EquivalentClass" c.EquivalentClass
          uriListField "SeeAlso" c.SeeAlso
          "Properties", AstRender.listExpr (c.Properties |> List.map propExpr) ]

let private ontologyExpr (onto: OntologyDecl) =
    AstRender.recordExpr
        [ "Classes", AstRender.listExpr (onto.Classes |> List.map classExpr)
          uriListField "ContextBases" onto.ContextBases ]

// ── Public API ───────────────────────────────────────────────────────────────

/// Emit a GeneratedLinkedData F# module from a vocabulary registry and lock file.
///
/// moduleName — the F# module name to emit (e.g. "TicTacToe.GeneratedLinkedData")
/// registry   — the VocabularyRegistry providing prefix→URI mappings, Using set,
///              SeeAlso, and EquivalentClasses (keyed by FSharpType FullName string)
/// lock       — the resolved lock file
///
/// Returns Ok with the F# source string, or Error if any IRI references an unknown prefix.
let emit (moduleName: string) (registry: VocabularyRegistry) (lock: LockFile) : Result<string, string> =
    if String.IsNullOrWhiteSpace moduleName then
        invalidArg (nameof moduleName) "moduleName must not be empty"

    match ResolvedModel.build registry lock with
    | Error e -> Error e
    | Ok model ->
        match contextBases model with
        | Error e -> Error e
        | Ok bases ->
            let onto =
                { projectOntology model with
                    ContextBases = bases }

            let decls =
                [ AstRender.valueDecl "ontology" "OntologyDecl" (ontologyExpr onto)
                  AstRender.valueDecl
                      "graph"
                      "VDS.RDF.IGraph"
                      (AstRender.appExpr "Ontology.toGraph" (AstRender.rawExpr "ontology"))
                  AstRender.valueDecl
                      "jsonLdContext"
                      "string"
                      (AstRender.appExpr "Ontology.toJsonLdContext" (AstRender.rawExpr "ontology")) ]

            Ok(AstRender.formatModule moduleName None [ "Frank.Semantic"; "Frank.LinkedData" ] decls)
