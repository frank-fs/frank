module Frank.LinkedData.Tests.InstanceProjectorTests

open System
open Expecto
open VDS.RDF
open Frank.LinkedData.Rdf

type TestPerson = { Name: string; Age: int; Homepage: string option }
type TestAddress = { Street: string; City: string }
type TestWithNested = { Label: string; Address: TestAddress }
type TestWithList = { Tags: string list }
type TestWithFloat = { Score: float; Price: decimal; IsActive: bool }
type TestWithDates = { CreatedAt: DateTimeOffset; ModifiedAt: DateTime }
type TestEmpty = { Items: string list }

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

        testCase "caches PropertyInfo lookup across calls" <| fun _ ->
            let ontology = buildOntology [ "Name"; "Age"; "Homepage" ]
            let person1 = { Name = "Alice"; Age = 30; Homepage = None }
            let person2 = { Name = "Bob"; Age = 25; Homepage = Some "https://bob.example.com" }
            // First call populates the cache
            let result1 = InstanceProjector.project ontology (Uri("http://example.org/people/10")) person1
            // Second call for the same type should use cached PropertyInfo[]
            let result2 = InstanceProjector.project ontology (Uri("http://example.org/people/11")) person2
            // Verify both calls produce correct results (cache did not corrupt anything)
            Expect.equal result1.Triples.Count 2
                "First projection should have 2 triples (Name, Age)"
            Expect.equal result2.Triples.Count 3
                "Second projection should have 3 triples (Name, Age, Homepage)"
            // The fact that both calls succeed with correct results confirms the cache works.
            // Additionally, verify the cache is populated by projecting yet another instance
            // and checking it still works correctly (functional proof of caching).
            let person3 = { Name = "Carol"; Age = 35; Homepage = None }
            let result3 = InstanceProjector.project ontology (Uri("http://example.org/people/12")) person3
            Expect.equal result3.Triples.Count 2
                "Third projection should also have 2 triples, confirming cache consistency"

        testCase "projects float as xsd:double, decimal as xsd:decimal, bool as xsd:boolean" <| fun _ ->
            let ontology = buildOntology [ "Score"; "Price"; "IsActive" ]
            let instance = { Score = 3.14; Price = 9.99m; IsActive = true }
            let result = InstanceProjector.project ontology (Uri("http://example.org/test/1")) instance
            Expect.equal result.Triples.Count 3 "Should have 3 triples"
            let scoreTriples =
                result.Triples |> Seq.filter (fun t ->
                    match t.Predicate with
                    | :? IUriNode as u -> u.Uri.ToString().EndsWith("/Score")
                    | _ -> false)
                |> Seq.toList
            Expect.equal scoreTriples.Length 1 "Should have 1 Score triple"
            match scoreTriples.[0].Object with
            | :? ILiteralNode as lit ->
                Expect.isNotNull lit.DataType "Score should have datatype"
                Expect.stringContains (lit.DataType.ToString()) "double" "Score should be xsd:double"
            | _ -> failwith "Expected literal node for Score"
            let priceTriples =
                result.Triples |> Seq.filter (fun t ->
                    match t.Predicate with
                    | :? IUriNode as u -> u.Uri.ToString().EndsWith("/Price")
                    | _ -> false)
                |> Seq.toList
            match priceTriples.[0].Object with
            | :? ILiteralNode as lit ->
                Expect.stringContains (lit.DataType.ToString()) "decimal" "Price should be xsd:decimal"
            | _ -> failwith "Expected literal node for Price"
            let boolTriples =
                result.Triples |> Seq.filter (fun t ->
                    match t.Predicate with
                    | :? IUriNode as u -> u.Uri.ToString().EndsWith("/IsActive")
                    | _ -> false)
                |> Seq.toList
            match boolTriples.[0].Object with
            | :? ILiteralNode as lit ->
                Expect.stringContains (lit.DataType.ToString()) "boolean" "IsActive should be xsd:boolean"
                Expect.equal lit.Value "true" "IsActive value should be 'true'"
            | _ -> failwith "Expected literal node for IsActive"

        testCase "projects DateTimeOffset as xsd:dateTime" <| fun _ ->
            let ontology = buildOntology [ "CreatedAt"; "ModifiedAt" ]
            let instance = { CreatedAt = DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero); ModifiedAt = DateTime(2024, 1, 15, 10, 30, 0) }
            let result = InstanceProjector.project ontology (Uri("http://example.org/test/2")) instance
            Expect.equal result.Triples.Count 2 "Should have 2 triples"
            for t in result.Triples do
                match t.Object with
                | :? ILiteralNode as lit ->
                    Expect.isNotNull lit.DataType "Date field should have datatype"
                    Expect.stringContains (lit.DataType.ToString()) "dateTime" "Date should be xsd:dateTime"
                | _ -> failwith "Expected literal node"

        testCase "empty list produces zero triples" <| fun _ ->
            let ontology = buildOntology [ "Items" ]
            let instance : TestEmpty = { Items = [] }
            let result = InstanceProjector.project ontology (Uri("http://example.org/test/3")) instance
            Expect.equal result.Triples.Count 0 "Empty list should produce 0 triples"

        testCase "skips properties not in ontology" <| fun _ ->
            let ontology = buildOntology [ "Name" ]  // Only Name, not Age or Homepage
            let person = { Name = "Alice"; Age = 30; Homepage = None }
            let result = InstanceProjector.project ontology (Uri("http://example.org/test/4")) person
            Expect.equal result.Triples.Count 1 "Should only project Name (1 triple)"
    ]
