module Frank.Cli.Core.LinkedDataEmitter

open System
open Frank.Semantic
open Frank.Semantic.LockFile

// ── Prefix resolution ────────────────────────────────────────────────────────

/// Resolve the external base IRIs for the @context from the model's Using set and Prefixes map.
/// Iterates Set.toList (ascending) — identical order to the old buildContext loop.
/// Returns Error if any using prefix is not in Prefixes.
let internal contextBases (model: ResolvedModel) : Result<Uri list, string> =
    let rec loop (remaining: string list) (acc: Uri list) =
        match remaining with
        | [] -> Ok(List.rev acc)
        | prefix :: rest ->
            match Map.tryFind prefix model.Prefixes with
            | None -> Error $"using prefix '{prefix}' not found in Prefixes"
            | Some baseUri ->
                // stored un-trimmed; Ontology.toJsonLdContext trims trailing '/' at render
                loop rest (baseUri :: acc)

    loop (Set.toList model.Using) []

// ── OntologyDecl projection ──────────────────────────────────────────────────

let private toClassDecl (r: ResolvedResource) : ClassDecl option =
    r.ClassIri
    |> Option.map (fun classUri ->
        // Invariant: Domain = classUri for every property emitted by this function.
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

/// Emit a System.Uri expression in absolute form. The ontology/graph/jsonLdContext
/// built from this record are module-level values evaluated once at load time,
/// before any HTTP request exists — there is no live request origin to resolve a
/// relative Uri against, and Ontology.toGraph/toJsonLdContext require absolute Uris
/// (#396). A vocabulary's own base URI is fixed by declaration, so the absolute
/// form is the correct emission for every field, owned prefix or not (#396 round 4).
let private uriExprFor (u: Uri) =
    AstRender.appExpr "System.Uri" (AstRender.strExpr u.AbsoluteUri)

let private uriField (name: string) (u: Uri) = name, uriExprFor u

let private renderUriOpt (u: Uri) = AstRender.parenExpr (uriExprFor u)

let private optUriField (name: string) (u: Uri option) =
    name, AstRender.optionExpr renderUriOpt u

let private uriListField (name: string) (us: Uri list) =
    name, AstRender.listExpr (us |> List.map uriExprFor)

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
    AstRender.validateModuleName moduleName
    |> Result.bind (fun () -> ResolvedModel.build registry lock)
    |> Result.bind (fun model ->
        contextBases model
        |> Result.map (fun ctxBases ->
            let onto =
                { projectOntology model with
                    ContextBases = ctxBases }

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

            AstRender.formatModule
                moduleName
                (Some AstRender.autoGeneratedHeader)
                [ "Frank.Semantic"; "Frank.LinkedData" ]
                decls))
