module Frank.LinkedData.Tests.InstanceProjectorTests

open System
open Expecto
open VDS.RDF
open Frank.LinkedData.Rdf

type TestPerson = { Name: string; Age: int; Homepage: string option }
type TestAddress = { Street: string; City: string }
type TestWithNested = { Label: string; Address: TestAddress }
type TestWithList = { Tags: string list }

/// Build a minimal ontology graph with property URI nodes for given local names.
let private buildOntology (propertyNames: string list) =
    let g = new Graph()
    for name in propertyNames do
        let uri = UriFactory.Root.Create(sprintf "http://example.org/api/properties/Test/%s" name)
        let node = g.CreateUriNode(uri)
        // Assert a dummy triple so the node is discoverable in the graph's Triples
        let rdfType = g.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"))
        let owlProp = g.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/2002/07/owl#DatatypeProperty"))
        g.Assert(Triple(node, rdfType, owlProp)) |> ignore
    g

[<Tests>]
let tests =
    testList "InstanceProjector" [
        testCase "projects string and int fields" <| fun _ ->
            let ontology = buildOntology [ "Name"; "Age"; "Homepage" ]
            let person = { Name = "Alice"; Age = 30; Homepage = None }
            let result = InstanceProjector.project ontology (Uri("http://example.org/people/1")) person
            // Name + Age = 2 triples (Homepage is None, skipped)
            Expect.equal result.Triples.Count 2
                "Should have 2 triples for Name and Age"

        testCase "skips None option fields" <| fun _ ->
            let ontology = buildOntology [ "Name"; "Age"; "Homepage" ]
            let person = { Name = "Bob"; Age = 25; Homepage = None }
            let result = InstanceProjector.project ontology (Uri("http://example.org/people/2")) person
            // Verify no triple with Homepage predicate
            let homepageUri = "http://example.org/api/properties/Test/Homepage"
            let hasHomepage =
                result.Triples
                |> Seq.exists (fun t ->
                    match t.Predicate with
                    | :? IUriNode as u -> u.Uri.ToString() = homepageUri
                    | _ -> false)
            Expect.isFalse hasHomepage "Should not have a Homepage triple when None"

        testCase "emits triple for Some option field" <| fun _ ->
            let ontology = buildOntology [ "Name"; "Age"; "Homepage" ]
            let person = { Name = "Carol"; Age = 28; Homepage = Some "https://example.com" }
            let result = InstanceProjector.project ontology (Uri("http://example.org/people/3")) person
            Expect.equal result.Triples.Count 3
                "Should have 3 triples (Name, Age, Homepage)"

        testCase "handles nested record as blank node" <| fun _ ->
            let ontology = buildOntology [ "Label"; "Address"; "Street"; "City" ]
            let instance = { Label = "Office"; Address = { Street = "123 Main St"; City = "Springfield" } }
            let result = InstanceProjector.project ontology (Uri("http://example.org/places/1")) instance
            // Label triple + Address->BlankNode triple + Street triple + City triple = 4
            Expect.equal result.Triples.Count 4
                "Should have 4 triples (Label, Address->blank, Street, City)"
            // Verify at least one blank node object
            let hasBlank =
                result.Triples
                |> Seq.exists (fun t ->
                    match t.Object with
                    | :? IBlankNode -> true
                    | _ -> false)
            Expect.isTrue hasBlank "Should have a blank node for nested record"

        testCase "emits one triple per list element" <| fun _ ->
            let ontology = buildOntology [ "Tags" ]
            let instance = { Tags = ["a"; "b"; "c"] }
            let result = InstanceProjector.project ontology (Uri("http://example.org/items/1")) instance
            Expect.equal result.Triples.Count 3
                "Should have 3 triples for 3 list elements"
    ]
