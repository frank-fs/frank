namespace Frank.LinkedData

open System
open VDS.RDF
open FsToolkit.ErrorHandling
open Frank.LinkedData.Rdf

/// Runtime configuration loaded at startup for linked data content negotiation.
type LinkedDataConfig =
    { OntologyGraph: IGraph
      ShapesGraph: IGraph
      BaseUri: string
      Manifest: SemanticManifest }

module LinkedDataConfig =
    open System.Reflection

    let private validateVersion (semantics: LoadedSemantics) =
        if String.IsNullOrEmpty(semantics.Manifest.Version) then
            Error "Manifest version is empty"
        else
            Ok semantics

    let private validateBaseUri (semantics: LoadedSemantics) =
        match Uri.TryCreate(semantics.Manifest.BaseUri, UriKind.Absolute) with
        | false, _ -> Error(sprintf "Manifest baseUri is not a valid URI: %s" semantics.Manifest.BaseUri)
        | true, _ -> Ok semantics

    let loadConfig (assembly: Assembly) : Result<LinkedDataConfig, string> =
        GraphLoader.load assembly
        |> Result.bind validateVersion
        |> Result.bind validateBaseUri
        |> Result.map (fun semantics ->
            { OntologyGraph = semantics.OntologyGraph
              ShapesGraph = semantics.ShapesGraph
              BaseUri = semantics.Manifest.BaseUri
              Manifest = semantics.Manifest })
