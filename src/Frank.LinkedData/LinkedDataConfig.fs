namespace Frank.LinkedData

open System
open VDS.RDF
open Frank.LinkedData.Rdf

/// Runtime configuration loaded at startup for linked data content negotiation.
type LinkedDataConfig =
    { OntologyGraph : IGraph
      ShapesGraph   : IGraph
      BaseUri       : string
      Manifest      : SemanticManifest }

module LinkedDataConfig =
    open System.Reflection

    let loadConfig (assembly: Assembly) : Result<LinkedDataConfig, string> =
        match GraphLoader.load assembly with
        | Error e -> Error e
        | Ok semantics ->
            if String.IsNullOrEmpty(semantics.Manifest.Version) then
                Error "Manifest version is empty"
            else
                match Uri.TryCreate(semantics.Manifest.BaseUri, UriKind.Absolute) with
                | false, _ ->
                    Error (sprintf "Manifest baseUri is not a valid URI: %s" semantics.Manifest.BaseUri)
                | true, _ ->
                    Ok { OntologyGraph = semantics.OntologyGraph
                         ShapesGraph   = semantics.ShapesGraph
                         BaseUri       = semantics.Manifest.BaseUri
                         Manifest      = semantics.Manifest }
