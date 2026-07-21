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

/// `bases` are the app's own declared-only prefix base URIs (EmitterShared.declaredOnlyBases)
/// — a Uri whose authority matches one is rendered host-relative (path+fragment only, e.g.
/// "/tictactoe#square"), deferring resolution to a real deployed origin supplied at call time
/// via Ontology.toGraph/toJsonLdContext's baseUri parameter (#396 round 5). Every other Uri
/// (genuinely external vocab — schema.org, Wikidata) is rendered absolute, unchanged (#396
/// round 4). Uri-expression rendering itself is EmitterShared.uriExprFor — the ONE place both
/// this module and SemanticModelEmitter build it (#415, constitution #8).
let private uriField (bases: Set<string>) (name: string) (u: Uri) = name, EmitterShared.uriExprFor bases u

let private renderUriOpt (bases: Set<string>) (u: Uri) =
    AstRender.parenExpr (EmitterShared.uriExprFor bases u)

let private optUriField (bases: Set<string>) (name: string) (u: Uri option) =
    name, AstRender.optionExpr (renderUriOpt bases) u

let private uriListField (bases: Set<string>) (name: string) (us: Uri list) =
    name, AstRender.listExpr (us |> List.map (EmitterShared.uriExprFor bases))

let private propExpr (bases: Set<string>) (p: PropertyDecl) =
    AstRender.recordExpr [ uriField bases "Iri" p.Iri; uriField bases "Domain" p.Domain ]

let private classExpr (bases: Set<string>) (c: ClassDecl) =
    AstRender.recordExpr
        [ uriField bases "Iri" c.Iri
          optUriField bases "EquivalentClass" c.EquivalentClass
          uriListField bases "SeeAlso" c.SeeAlso
          "Properties", AstRender.listExpr (c.Properties |> List.map (propExpr bases)) ]

let private ontologyExpr (bases: Set<string>) (onto: OntologyDecl) =
    AstRender.recordExpr
        [ "Classes", AstRender.listExpr (onto.Classes |> List.map (classExpr bases))
          uriListField bases "ContextBases" onto.ContextBases ]

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

            let bases = EmitterShared.declaredOnlyBases lock

            let someBaseUri =
                AstRender.parenExpr (AstRender.someExpr (AstRender.rawExpr "baseUri"))

            let decls =
                [ AstRender.valueDecl "ontology" "OntologyDecl" (ontologyExpr bases onto)
                  AstRender.funcDecl
                      "graphFor"
                      "baseUri"
                      "System.Uri"
                      "VDS.RDF.IGraph"
                      (AstRender.appExprN "Ontology.toGraph" [ someBaseUri; AstRender.rawExpr "ontology" ])
                  AstRender.funcDecl
                      "jsonLdContextFor"
                      "baseUri"
                      "System.Uri"
                      "string"
                      (AstRender.appExprN "Ontology.toJsonLdContext" [ someBaseUri; AstRender.rawExpr "ontology" ]) ]

            AstRender.formatModule
                moduleName
                (Some AstRender.autoGeneratedHeader)
                [ "Frank.Semantic"; "Frank.LinkedData" ]
                decls))
