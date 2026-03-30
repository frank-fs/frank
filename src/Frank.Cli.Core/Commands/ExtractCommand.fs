namespace Frank.Cli.Core.Commands

open System
open System.IO
open VDS.RDF
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis
open Frank.Resources.Model
open Frank.Cli.Core.Extraction
open Frank.Cli.Core.Extraction.UriHelpers
open Frank.Cli.Core.State

type private StateSourceLocation = Frank.Cli.Core.State.SourceLocation

/// Orchestrates the extract command: AST analysis -> type mapping -> state persistence.
module ExtractCommand =

    type OntologySummary =
        { ClassCount: int
          PropertyCount: int
          AlignedCount: int
          UnalignedCount: int }

    type ShapesSummary =
        { ShapeCount: int
          ConstraintCount: int }

    type ExtractResult =
        { OntologySummary: OntologySummary
          ShapesSummary: ShapesSummary
          UnmappedTypes: UnmappedType list
          StateFilePath: string }

    /// Dependency injection record for pipeline steps, enabling testability.
    type ExtractPipeline =
        { LoadProject: string -> Async<Result<LoadedProject, string>>
          AnalyzeAst: ParsedInput list -> AnalyzedResource list
          AnalyzeTypes: FSharpCheckProjectResults -> AnalyzedType list
          MapTypes: TypeMapper.MappingConfig -> AnalyzedType list -> IGraph
          MapRoutes: TypeMapper.MappingConfig -> AnalyzedResource list -> AnalyzedType list -> IGraph
          MapCapabilities: TypeMapper.MappingConfig -> AnalyzedResource list -> IGraph
          GenerateShapes: TypeMapper.MappingConfig -> AnalyzedType list -> IGraph
          AlignVocabularies: TypeMapper.MappingConfig -> IGraph -> IGraph
          SaveState: string -> ExtractionState -> Result<unit, string> }

    /// Default pipeline using the real implementations.
    let defaultPipeline: ExtractPipeline =
        { LoadProject = ProjectLoader.loadProject
          AnalyzeAst = AstAnalyzer.analyzeFiles
          AnalyzeTypes = TypeAnalyzer.analyzeTypes
          MapTypes = TypeMapper.mapTypes
          MapRoutes = RouteMapper.mapRoutes
          MapCapabilities = CapabilityMapper.mapCapabilities
          GenerateShapes = ShapeGenerator.generateShapes
          AlignVocabularies = VocabularyAligner.alignVocabularies
          SaveState = ExtractionState.save }

    let private countByType (graph: IGraph) (typeUri: Uri) : int =
        let rdfTypeNode = createUriNode graph (Uri Rdf.Type)
        let typeNode = createUriNode graph typeUri

        triplesWithPredicate graph rdfTypeNode
        |> Seq.filter (fun t ->
            match t.Object with
            | :? IUriNode as on -> on.Uri = typeUri
            | _ -> false)
        |> Seq.length

    let private mergeGraphs (target: IGraph) (source: IGraph) = target.Merge(source)

    let private countAligned (graph: IGraph) : int =
        let equivPropNode = createUriNode graph (Uri Owl.EquivalentProperty)
        let equivClassNode = createUriNode graph (Uri Owl.EquivalentClass)

        let propAligned = triplesWithPredicate graph equivPropNode |> Seq.length
        let classAligned = triplesWithPredicate graph equivClassNode |> Seq.length
        propAligned + classAligned

    let private detectUnmappedTypes
        (analyzedTypes: AnalyzedType list)
        (ontologyGraph: IGraph)
        (config: TypeMapper.MappingConfig)
        : UnmappedType list =
        analyzedTypes
        |> List.choose (fun t ->
            let classUri =
                UriHelpers.classUri (config.BaseUri.ToString().TrimEnd('/')) t.ShortName

            let node = getNode ontologyGraph classUri

            match node with
            | None ->
                let loc =
                    t.SourceLocation
                    |> Option.map (fun sl ->
                        { File = sl.File
                          Line = sl.Line
                          Column = sl.Column }
                        : StateSourceLocation)
                    |> Option.defaultValue (
                        { File = "unknown"
                          Line = 0
                          Column = 0 }
                        : StateSourceLocation
                    )

                Some
                    { TypeName = t.FullName
                      Reason = "No OWL class generated"
                      Location = loc }
            | Some _ -> None)

    let executeWithPipeline
        (pipeline: ExtractPipeline)
        (projectPath: string)
        (baseUri: Uri)
        (vocabularies: string list)
        : Async<Result<ExtractResult, string>> =
        async {
            try
                // Step 1: Load project
                let! loadResult = pipeline.LoadProject projectPath

                match loadResult with
                | Error e -> return Error $"Failed to load project: {e}"
                | Ok loaded ->

                    let config: TypeMapper.MappingConfig =
                        { BaseUri = baseUri
                          Vocabularies = vocabularies }

                    // Step 2: AST analysis
                    let parsedInputs = loaded.ParsedFiles |> List.map snd
                    let analyzedResources = pipeline.AnalyzeAst parsedInputs

                    // Step 3: Type analysis
                    let analyzedTypes = pipeline.AnalyzeTypes loaded.CheckResults

                    // Step 4: Type mapping
                    let typeGraph = pipeline.MapTypes config analyzedTypes

                    // Step 5: Route mapping
                    let routeGraph = pipeline.MapRoutes config analyzedResources analyzedTypes

                    // Step 6: Capability mapping
                    let capabilityGraph = pipeline.MapCapabilities config analyzedResources

                    // Step 7: Shape generation
                    let shapesGraph = pipeline.GenerateShapes config analyzedTypes

                    // Merge type, route, capability graphs into ontology, then dispose each source graph.
                    let ontologyGraph = createGraph ()
                    mergeGraphs ontologyGraph typeGraph
                    (typeGraph :> System.IDisposable).Dispose()
                    mergeGraphs ontologyGraph routeGraph
                    (routeGraph :> System.IDisposable).Dispose()
                    mergeGraphs ontologyGraph capabilityGraph
                    (capabilityGraph :> System.IDisposable).Dispose()

                    // Step 8: Vocabulary alignment
                    let alignedOntology = pipeline.AlignVocabularies config ontologyGraph

                    // Compute counts
                    let classCount = countByType alignedOntology (Uri Owl.Class)
                    let datatypePropCount = countByType alignedOntology (Uri Owl.DatatypeProperty)
                    let objectPropCount = countByType alignedOntology (Uri Owl.ObjectProperty)
                    let propertyCount = datatypePropCount + objectPropCount
                    let alignedCount = countAligned alignedOntology
                    let unalignedCount = (propertyCount + classCount) - alignedCount

                    let shapeCount = countByType shapesGraph (Uri Shacl.NodeShape)

                    let constraintCount =
                        let shaclPropertyNode = createUriNode shapesGraph (Uri Shacl.Path)
                        triplesWithPredicate shapesGraph shaclPropertyNode |> Seq.length

                    // Detect unmapped types
                    let unmappedTypes = detectUnmappedTypes analyzedTypes alignedOntology config

                    // Step 9: Persist state
                    let statePath = ExtractionState.defaultStatePath (Path.GetDirectoryName projectPath)
                    let sourceMap = Map.empty

                    let state: ExtractionState =
                        { Ontology = alignedOntology
                          Shapes = shapesGraph
                          SourceMap = sourceMap
                          Clarifications = Map.empty
                          Metadata =
                            { Timestamp = DateTimeOffset.UtcNow
                              SourceHash = ""
                              ToolVersion = "0.1.0"
                              BaseUri = baseUri
                              Vocabularies = vocabularies }
                          UnmappedTypes = unmappedTypes }

                    match pipeline.SaveState statePath state with
                    | Error e -> return Error $"Failed to save state: {e}"
                    | Ok() ->

                        return
                            Ok
                                { OntologySummary =
                                    { ClassCount = classCount
                                      PropertyCount = propertyCount
                                      AlignedCount = alignedCount
                                      UnalignedCount = unalignedCount }
                                  ShapesSummary =
                                    { ShapeCount = shapeCount
                                      ConstraintCount = constraintCount }
                                  UnmappedTypes = unmappedTypes
                                  StateFilePath = statePath }
            with ex ->
                return Error $"Extraction failed: {ex.Message}"
        }

    let execute
        (projectPath: string)
        (baseUri: Uri)
        (vocabularies: string list)
        : Async<Result<ExtractResult, string>> =
        executeWithPipeline defaultPipeline projectPath baseUri vocabularies
