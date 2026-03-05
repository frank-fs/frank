namespace Frank.Cli.Core.Rdf

open System
open VDS.RDF

/// F# wrappers around dotNetRdf types: Option conversions, DU node wrappers.
[<AutoOpen>]
module FSharpRdf =

    type RdfNode =
        | UriNode of Uri
        | LiteralNode of value: string * datatype: Uri option
        | BlankNode of id: string

    let createGraph () : IGraph =
        new Graph() :> IGraph

    let createUriNode (graph: IGraph) (uri: Uri) : INode =
        graph.CreateUriNode(uri)

    let createLiteralNode (graph: IGraph) (value: string) (datatype: Uri option) : INode =
        match datatype with
        | Some dt -> graph.CreateLiteralNode(value, dt)
        | None -> graph.CreateLiteralNode(value)

    let createBlankNode (graph: IGraph) (id: string) : INode =
        graph.CreateBlankNode(id)

    let assertTriple (graph: IGraph) (s: INode, p: INode, o: INode) : unit =
        graph.Assert(new Triple(s, p, o)) |> ignore

    let getNode (graph: IGraph) (uri: Uri) : INode option =
        let node = graph.GetUriNode(uri)
        if isNull node then None else Some(node :> INode)

    let triplesWithSubject (graph: IGraph) (node: INode) : Triple seq =
        graph.GetTriplesWithSubject(node)

    let triplesWithPredicate (graph: IGraph) (node: INode) : Triple seq =
        graph.GetTriplesWithPredicate(node)

    let triplesWithSubjectPredicate (graph: IGraph) (s: INode) (p: INode) : Triple seq =
        graph.GetTriplesWithSubjectPredicate(s, p)

    let toRdfNode (node: INode) : RdfNode =
        match node with
        | :? IUriNode as u -> UriNode u.Uri
        | :? ILiteralNode as l ->
            let dt =
                if isNull l.DataType then None else Some l.DataType
            LiteralNode(l.Value, dt)
        | :? IBlankNode as b -> BlankNode b.InternalID
        | _ -> failwithf "Unsupported node type: %A" (node.GetType())

    let fromRdfNode (graph: IGraph) (rdfNode: RdfNode) : INode =
        match rdfNode with
        | UriNode uri -> createUriNode graph uri
        | LiteralNode(value, datatype) -> createLiteralNode graph value datatype
        | BlankNode id -> createBlankNode graph id
