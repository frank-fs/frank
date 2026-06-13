module TicTacToe.Vocabulary

open VDS.RDF
open VDS.RDF.Shacl
open Frank.Discovery
open Frank.LinkedData.LinkedDataMiddleware
open Frank.Provenance.ProvenanceMiddleware

// IRIs from .frank/semantic-mappings.lock.json
let [<Literal>] SchemaBase = "https://schema.org/"
let [<Literal>] GameIri = "https://schema.org/Game"
let [<Literal>] MoveActionIri = "https://schema.org/MoveAction"
let [<Literal>] RowIndexIri = "https://schema.org/rowIndex"
let [<Literal>] ColIndexIri = "https://schema.org/columnIndex"
let [<Literal>] AgentIri = "https://schema.org/agent"
let [<Literal>] IdentifierIri = "https://schema.org/identifier"

// SHACL shapes for MoveAction — mirrors GeneratedValidation.fs
let buildShapesGraph () : ShapesGraph =
    let sh = "http://www.w3.org/ns/shacl#"
    let xsd = "http://www.w3.org/2001/XMLSchema#"
    let rdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"
    let g = new Graph()
    let uri s = g.CreateUriNode(UriFactory.Create(s))
    let litInt s = g.CreateLiteralNode(s, UriFactory.Create(xsd + "integer"))

    let shapeNode = uri "urn:shape:MoveAction"
    let rowProp = uri "urn:shape:MoveAction.Row"
    let colProp = uri "urn:shape:MoveAction.Col"

    g.Assert(Triple(shapeNode, uri rdfType, uri (sh + "NodeShape"))) |> ignore
    g.Assert(Triple(shapeNode, uri (sh + "targetClass"), uri MoveActionIri)) |> ignore
    g.Assert(Triple(shapeNode, uri (sh + "property"), rowProp)) |> ignore
    g.Assert(Triple(shapeNode, uri (sh + "property"), colProp)) |> ignore

    g.Assert(Triple(rowProp, uri rdfType, uri (sh + "PropertyShape"))) |> ignore
    g.Assert(Triple(rowProp, uri (sh + "path"), uri RowIndexIri)) |> ignore
    g.Assert(Triple(rowProp, uri (sh + "datatype"), uri (xsd + "integer"))) |> ignore
    g.Assert(Triple(rowProp, uri (sh + "minCount"), litInt "1")) |> ignore
    g.Assert(Triple(rowProp, uri (sh + "minInclusive"), litInt "0")) |> ignore
    g.Assert(Triple(rowProp, uri (sh + "maxInclusive"), litInt "2")) |> ignore

    g.Assert(Triple(colProp, uri rdfType, uri (sh + "PropertyShape"))) |> ignore
    g.Assert(Triple(colProp, uri (sh + "path"), uri ColIndexIri)) |> ignore
    g.Assert(Triple(colProp, uri (sh + "datatype"), uri (xsd + "integer"))) |> ignore
    g.Assert(Triple(colProp, uri (sh + "minCount"), litInt "1")) |> ignore
    g.Assert(Triple(colProp, uri (sh + "minInclusive"), litInt "0")) |> ignore
    g.Assert(Triple(colProp, uri (sh + "maxInclusive"), litInt "2")) |> ignore

    new ShapesGraph(g)

// IGraph for the Game type — mirrors GeneratedLinkedData.fs
let buildGraph () : IGraph =
    let g = new Graph()
    let uri s = g.CreateUriNode(UriFactory.Create(s))
    let owl = "http://www.w3.org/2002/07/owl#"
    let rdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"

    g.Assert(Triple(uri "urn:frank:type:TicTacToe.Game", uri rdfType, uri (owl + "Class"))) |> ignore
    g.Assert(Triple(uri "urn:frank:type:TicTacToe.Game", uri (owl + "equivalentClass"), uri GameIri)) |> ignore
    g.Assert(Triple(uri "urn:frank:type:TicTacToe.MoveRequest", uri rdfType, uri (owl + "Class"))) |> ignore
    g.Assert(Triple(uri "urn:frank:type:TicTacToe.MoveRequest", uri (owl + "equivalentClass"), uri MoveActionIri)) |> ignore

    g :> IGraph

let jsonLdContext = """{"@context": ["https://schema.org/"]}"""

// Discovery config — mirrors GeneratedDiscovery.fs
let buildDiscoveryConfig () : DiscoveryConfig =
    { ProfileBaseUri = "/alps"
      HomeRoute = "/"
      AlpsDescriptors =
        Map.ofList
          [ "TicTacToe.Game",
            [ { Id = "Game"; Type = "semantic"; Doc = Some "A TicTacToe game"; Href = Some GameIri }
              { Id = "identifier"; Type = "semantic"; Doc = None; Href = Some IdentifierIri }
              { Id = "MoveAction"; Type = "semantic"; Doc = Some "A move in the game"; Href = Some MoveActionIri }
              { Id = "rowIndex"; Type = "semantic"; Doc = Some "Row (0-2)"; Href = Some RowIndexIri }
              { Id = "columnIndex"; Type = "semantic"; Doc = Some "Column (0-2)"; Href = Some ColIndexIri }
              { Id = "agent"; Type = "semantic"; Doc = Some "Player (X or O)"; Href = Some AgentIri }
              { Id = "makeMove"; Type = "unsafe"; Doc = Some "Make a move"; Href = None } ] ]
      DescribedByLinks =
        Map.ofList [ "TicTacToe.Game", [ $"<{GameIri}>; rel=\"describedby\"" ] ] }

let provenanceConfig = ProvenanceConfig.Default
