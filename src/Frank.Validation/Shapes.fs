module Frank.Validation.Shapes

open System
open VDS.RDF
open VDS.RDF.Shacl
open Frank.Semantic

let private xsd =
    function
    | XsdInteger -> "xsd:integer"
    | XsdLong -> "xsd:long"
    | XsdDecimal -> "xsd:decimal"
    | XsdDouble -> "xsd:double"
    | XsdBoolean -> "xsd:boolean"
    | XsdString -> "xsd:string"
    | XsdDateTime -> "xsd:dateTime"

let private intLit (g: IGraph) (n: int) : INode =
    g.CreateLiteralNode(string n, UriFactory.Create "http://www.w3.org/2001/XMLSchema#integer")

let private addProperty (g: IGraph) (classNode: INode) (ri: int) (pi: int) (p: PropertyShape) : unit =
    let bn = g.CreateBlankNode(sprintf "bn_%d_%d" ri pi)
    Triples.assert3 g classNode (Triples.qnameNode g "sh:property") bn
    Triples.assert3 g bn (Triples.qnameNode g "sh:path") (Triples.uriNode g p.Path.AbsoluteUri)

    p.Datatype
    |> Option.iter (fun d -> Triples.assert3 g bn (Triples.qnameNode g "sh:datatype") (Triples.qnameNode g (xsd d)))

    Triples.assert3 g bn (Triples.qnameNode g "sh:minCount") (intLit g p.MinCount)

    p.MaxCount
    |> Option.iter (fun m -> Triples.assert3 g bn (Triples.qnameNode g "sh:maxCount") (intLit g m))

    p.Pattern
    |> Option.iter (fun pat -> Triples.assert3 g bn (Triples.qnameNode g "sh:pattern") (g.CreateLiteralNode pat))

let private addRdfList (g: IGraph) (ri: int) (members: INode list) : INode =
    let rdfFirst = Triples.qnameNode g "rdf:first"
    let rdfRest = Triples.qnameNode g "rdf:rest"
    let rdfNil = Triples.uriNode g "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"
    let count = List.length members

    let cell i =
        g.CreateBlankNode(sprintf "bn_%d_in_%d" ri i)

    members
    |> List.iteri (fun i m ->
        let c = cell i
        Triples.assert3 g c rdfFirst m
        Triples.assert3 g c rdfRest (if i = count - 1 then rdfNil else cell (i + 1)))

    cell 0

let private addShape (g: IGraph) (ri: int) (shape: ShapeDecl) : unit =
    let classIri =
        match shape with
        | RecordShape(c, _)
        | EnumShape(c, _) -> c.AbsoluteUri

    let classNode = Triples.uriNode g classIri
    Triples.assert3 g classNode (Triples.qnameNode g "rdf:type") (Triples.qnameNode g "sh:NodeShape")
    Triples.assert3 g classNode (Triples.qnameNode g "sh:targetClass") classNode

    match shape with
    | RecordShape(_, props) -> props |> List.iteri (fun pi p -> addProperty g classNode ri pi p)
    | EnumShape(_, cases) ->
        let members =
            NonEmptyList.toList cases |> List.map (fun u -> Triples.uriNode g u.AbsoluteUri)

        let head = addRdfList g ri members
        Triples.assert3 g classNode (Triples.qnameNode g "sh:in") head

/// THE single place SHACL triples are built. Total over ShapeDecl, correct by construction.
let toShapesGraph (shapes: ShapeDecl list) : ShapesGraph =
    let g = new Graph() :> IGraph
    g.NamespaceMap.AddNamespace("sh", UriFactory.Create "http://www.w3.org/ns/shacl#")
    g.NamespaceMap.AddNamespace("xsd", UriFactory.Create "http://www.w3.org/2001/XMLSchema#")
    g.NamespaceMap.AddNamespace("rdf", UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#")
    shapes |> List.iteri (fun ri s -> addShape g ri s)
    new ShapesGraph(g)
