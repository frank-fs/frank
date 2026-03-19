module Frank.Cli.Core.Tests.VocabularyAlignerTests

open System
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Extraction.TypeMapper
open Frank.Cli.Core.Extraction.VocabularyAligner

let assertValidTurtle (graph: IGraph) =
    let writer = VDS.RDF.Writing.CompressingTurtleWriter()
    let turtle = VDS.RDF.Writing.StringWriter.Write(graph, writer)
    let parser = VDS.RDF.Parsing.TurtleParser()
    let roundTrip = new VDS.RDF.Graph()
    use reader = new System.IO.StringReader(turtle)
    parser.Load(roundTrip, reader)

let hasTriple (graph: IGraph) (s: Uri) (p: Uri) (o: Uri) =
    graph.Triples
    |> Seq.exists (fun t ->
        match t.Subject, t.Predicate, t.Object with
        | (:? IUriNode as sn), (:? IUriNode as pn), (:? IUriNode as on) -> sn.Uri = s && pn.Uri = p && on.Uri = o
        | _ -> false)

let baseUri = Uri "http://example.org/api"

let configWithSchema: MappingConfig =
    { BaseUri = baseUri
      Vocabularies = [ "schema.org"; "hydra" ] }

let configWithoutSchema: MappingConfig =
    { BaseUri = baseUri
      Vocabularies = [ "hydra" ] }

/// Helper: create a graph with a property that has a given label
let makeGraphWithProperty (propUri: Uri) (label: string) (isObject: bool) =
    let graph = createGraph ()
    let propNode = createUriNode graph propUri
    let rdfType = createUriNode graph (Uri Rdf.Type)

    let propTypeUri =
        if isObject then
            Uri Owl.ObjectProperty
        else
            Uri Owl.DatatypeProperty

    assertTriple graph (propNode, rdfType, createUriNode graph propTypeUri)
    assertTriple graph (propNode, createUriNode graph (Uri Rdfs.Label), createLiteralNode graph label None)
    graph

/// Helper: create a graph with an owl:Class that has a given label
let makeGraphWithClass (classUri: Uri) (label: string) =
    let graph = createGraph ()
    let classNode = createUriNode graph classUri
    let rdfType = createUriNode graph (Uri Rdf.Type)
    assertTriple graph (classNode, rdfType, createUriNode graph (Uri Owl.Class))
    assertTriple graph (classNode, createUriNode graph (Uri Rdfs.Label), createLiteralNode graph label None)
    graph

[<Tests>]
let tests =
    testList
        "VocabularyAligner"
        [ testCase "email property gets owl:equivalentProperty schema:email"
          <| fun _ ->
              let propUri = Uri "http://example.org/api/properties/Person/email"
              let graph = makeGraphWithProperty propUri "email" false
              let result = alignVocabularies configWithSchema graph

              Expect.isTrue
                  (hasTriple result propUri (Uri Owl.EquivalentProperty) (Uri SchemaOrg.Email))
                  "email should get owl:equivalentProperty schema:email"

              assertValidTurtle result

          testCase "name property gets owl:equivalentProperty schema:name"
          <| fun _ ->
              let propUri = Uri "http://example.org/api/properties/Product/name"
              let graph = makeGraphWithProperty propUri "name" false
              let result = alignVocabularies configWithSchema graph

              Expect.isTrue
                  (hasTriple result propUri (Uri Owl.EquivalentProperty) (Uri SchemaOrg.Name))
                  "name should get owl:equivalentProperty schema:name"

              assertValidTurtle result

          testCase "description property gets owl:equivalentProperty schema:description"
          <| fun _ ->
              let propUri = Uri "http://example.org/api/properties/Item/description"
              let graph = makeGraphWithProperty propUri "description" false
              let result = alignVocabularies configWithSchema graph

              Expect.isTrue
                  (hasTriple result propUri (Uri Owl.EquivalentProperty) (Uri SchemaOrg.Description))
                  "description should get owl:equivalentProperty schema:description"

              assertValidTurtle result

          testCase "price property gets owl:equivalentProperty schema:price"
          <| fun _ ->
              let propUri = Uri "http://example.org/api/properties/Product/price"
              let graph = makeGraphWithProperty propUri "price" false
              let result = alignVocabularies configWithSchema graph

              Expect.isTrue
                  (hasTriple result propUri (Uri Owl.EquivalentProperty) (Uri SchemaOrg.Price))
                  "price should get owl:equivalentProperty schema:price"

              assertValidTurtle result

          testCase "url property gets owl:equivalentProperty schema:url"
          <| fun _ ->
              let propUri = Uri "http://example.org/api/properties/Link/url"
              let graph = makeGraphWithProperty propUri "url" false
              let result = alignVocabularies configWithSchema graph

              Expect.isTrue
                  (hasTriple result propUri (Uri Owl.EquivalentProperty) (Uri SchemaOrg.Url))
                  "url should get owl:equivalentProperty schema:url"

              assertValidTurtle result

          testCase "createdAt property gets owl:equivalentProperty schema:dateCreated"
          <| fun _ ->
              let propUri = Uri "http://example.org/api/properties/Item/createdAt"
              let graph = makeGraphWithProperty propUri "createdAt" false
              let result = alignVocabularies configWithSchema graph

              Expect.isTrue
                  (hasTriple result propUri (Uri Owl.EquivalentProperty) (Uri SchemaOrg.DateCreated))
                  "createdAt should get owl:equivalentProperty schema:dateCreated"

              assertValidTurtle result

          testCase "no alignment when schema.org not in config"
          <| fun _ ->
              let propUri = Uri "http://example.org/api/properties/Person/email"
              let graph = makeGraphWithProperty propUri "email" false
              let result = alignVocabularies configWithoutSchema graph

              Expect.isFalse
                  (hasTriple result propUri (Uri Owl.EquivalentProperty) (Uri SchemaOrg.Email))
                  "email should NOT get alignment when schema.org not in config"

              assertValidTurtle result

          testCase "unrecognized field name gets no alignment"
          <| fun _ ->
              let propUri = Uri "http://example.org/api/properties/Product/sku"
              let graph = makeGraphWithProperty propUri "sku" false
              let result = alignVocabularies configWithSchema graph

              let hasAnyEquivalent =
                  result.Triples
                  |> Seq.exists (fun t ->
                      match t.Subject, t.Predicate with
                      | (:? IUriNode as sn), (:? IUriNode as pn) ->
                          sn.Uri = propUri && pn.Uri = Uri Owl.EquivalentProperty
                      | _ -> false)

              Expect.isFalse hasAnyEquivalent "unrecognized field should have no equivalentProperty"

              assertValidTurtle result

          testCase "Person class gets owl:equivalentClass schema:Person"
          <| fun _ ->
              let classUri = Uri "http://example.org/api/classes/Person"
              let graph = makeGraphWithClass classUri "Person"
              let result = alignVocabularies configWithSchema graph

              Expect.isTrue
                  (hasTriple result classUri (Uri Owl.EquivalentClass) (Uri SchemaOrg.Person))
                  "Person class should get owl:equivalentClass schema:Person"

              assertValidTurtle result

          testCase "User class gets owl:equivalentClass schema:Person"
          <| fun _ ->
              let classUri = Uri "http://example.org/api/classes/User"
              let graph = makeGraphWithClass classUri "User"
              let result = alignVocabularies configWithSchema graph

              Expect.isTrue
                  (hasTriple result classUri (Uri Owl.EquivalentClass) (Uri SchemaOrg.Person))
                  "User class should get owl:equivalentClass schema:Person"

              assertValidTurtle result

          testCase "Organization class gets owl:equivalentClass schema:Organization"
          <| fun _ ->
              let classUri = Uri "http://example.org/api/classes/Organization"
              let graph = makeGraphWithClass classUri "Organization"
              let result = alignVocabularies configWithSchema graph

              Expect.isTrue
                  (hasTriple result classUri (Uri Owl.EquivalentClass) (Uri SchemaOrg.Organization))
                  "Organization class should get owl:equivalentClass schema:Organization"

              assertValidTurtle result

          testCase "Product class gets owl:equivalentClass schema:Product"
          <| fun _ ->
              let classUri = Uri "http://example.org/api/classes/Product"
              let graph = makeGraphWithClass classUri "Product"
              let result = alignVocabularies configWithSchema graph

              Expect.isTrue
                  (hasTriple result classUri (Uri Owl.EquivalentClass) (Uri SchemaOrg.Product))
                  "Product class should get owl:equivalentClass schema:Product"

              assertValidTurtle result

          testCase "no class alignment when schema.org not in config"
          <| fun _ ->
              let classUri = Uri "http://example.org/api/classes/Person"
              let graph = makeGraphWithClass classUri "Person"
              let result = alignVocabularies configWithoutSchema graph

              Expect.isFalse
                  (hasTriple result classUri (Uri Owl.EquivalentClass) (Uri SchemaOrg.Person))
                  "Person class should NOT get alignment when schema.org not in config"

              assertValidTurtle result

          testCase "unrecognized class name gets no alignment"
          <| fun _ ->
              let classUri = Uri "http://example.org/api/classes/Widget"
              let graph = makeGraphWithClass classUri "Widget"
              let result = alignVocabularies configWithSchema graph

              let hasAnyEquivalent =
                  result.Triples
                  |> Seq.exists (fun t ->
                      match t.Subject, t.Predicate with
                      | (:? IUriNode as sn), (:? IUriNode as pn) ->
                          sn.Uri = classUri && pn.Uri = Uri Owl.EquivalentClass
                      | _ -> false)

              Expect.isFalse hasAnyEquivalent "unrecognized class should have no equivalentClass"

              assertValidTurtle result ]
