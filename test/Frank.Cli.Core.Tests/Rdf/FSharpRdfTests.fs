module Frank.Cli.Core.Tests.FSharpRdfTests

open System
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf

[<Tests>]
let tests =
    testList
        "FSharpRdf"
        [ testCase "create graph and assert triples, query with triplesWithSubject"
          <| fun _ ->
              let graph = createGraph ()
              let subj = createUriNode graph (Uri "http://example.org/person/1")
              let pred = createUriNode graph (Uri "http://example.org/name")
              let obj = createLiteralNode graph "Alice" None
              assertTriple graph (subj, pred, obj)

              let pred2 = createUriNode graph (Uri "http://example.org/age")

              let obj2 =
                  createLiteralNode graph "30" (Some(Uri "http://www.w3.org/2001/XMLSchema#integer"))

              assertTriple graph (subj, pred2, obj2)

              let pred3 = createUriNode graph (Uri "http://example.org/knows")
              let obj3 = createUriNode graph (Uri "http://example.org/person/2")
              assertTriple graph (subj, pred3, obj3)

              let triples = triplesWithSubject graph subj |> Seq.toList
              Expect.equal triples.Length 3 "Should have 3 triples for subject"

          testCase "getNode returns Some for existing URI"
          <| fun _ ->
              let graph = createGraph ()
              let uri = Uri "http://example.org/thing"
              let _ = createUriNode graph uri
              let pred = createUriNode graph (Uri "http://example.org/p")
              let obj = createLiteralNode graph "val" None
              assertTriple graph (createUriNode graph uri, pred, obj)

              let result = getNode graph uri
              Expect.isSome result "Should find existing node"

          testCase "getNode returns None for missing URI"
          <| fun _ ->
              let graph = createGraph ()
              let result = getNode graph (Uri "http://example.org/missing")
              Expect.isNone result "Should return None for missing node"

          testCase "DU roundtrip: UriNode"
          <| fun _ ->
              let graph = createGraph ()
              let uri = Uri "http://example.org/roundtrip"
              let original = RdfNode.UriNode uri
              let inode = fromRdfNode graph original
              let roundtripped = toRdfNode inode
              Expect.equal roundtripped original "UriNode should roundtrip"

          testCase "DU roundtrip: LiteralNode without datatype"
          <| fun _ ->
              let graph = createGraph ()
              let original = RdfNode.LiteralNode("hello", None)
              let inode = fromRdfNode graph original
              let roundtripped = toRdfNode inode
              // dotNetRdf may assign xsd:string datatype to plain literals
              match roundtripped with
              | RdfNode.LiteralNode(v, _) -> Expect.equal v "hello" "Value should match"
              | _ -> failtest "Expected LiteralNode"

          testCase "DU roundtrip: LiteralNode with datatype"
          <| fun _ ->
              let graph = createGraph ()
              let dt = Uri "http://www.w3.org/2001/XMLSchema#integer"
              let original = RdfNode.LiteralNode("42", Some dt)
              let inode = fromRdfNode graph original
              let roundtripped = toRdfNode inode
              Expect.equal roundtripped original "LiteralNode with datatype should roundtrip"

          testCase "DU roundtrip: BlankNode"
          <| fun _ ->
              let graph = createGraph ()
              let original = RdfNode.BlankNode "b0"
              let inode = fromRdfNode graph original
              let roundtripped = toRdfNode inode
              Expect.equal roundtripped original "BlankNode should roundtrip"

          testCase "triplesWithPredicate returns matching triples"
          <| fun _ ->
              let graph = createGraph ()
              let pred = createUriNode graph (Uri "http://example.org/name")
              let s1 = createUriNode graph (Uri "http://example.org/1")
              let s2 = createUriNode graph (Uri "http://example.org/2")
              let o1 = createLiteralNode graph "Alice" None
              let o2 = createLiteralNode graph "Bob" None
              assertTriple graph (s1, pred, o1)
              assertTriple graph (s2, pred, o2)

              let triples = triplesWithPredicate graph pred |> Seq.toList
              Expect.equal triples.Length 2 "Should have 2 triples with predicate" ]
