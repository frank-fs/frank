module Frank.Cli.Core.Tests.DiffEngineTests

open System
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.State

[<Tests>]
let tests =
    testList "DiffEngine" [
        testCase "detects added and removed triples" <| fun _ ->
            let oldGraph = createGraph ()
            let s = createUriNode oldGraph (Uri "http://example.org/A")
            let p = createUriNode oldGraph (Uri "http://example.org/name")
            let o = createLiteralNode oldGraph "Alice" None
            assertTriple oldGraph (s, p, o)

            let removedP = createUriNode oldGraph (Uri "http://example.org/age")
            let removedO = createLiteralNode oldGraph "30" None
            assertTriple oldGraph (s, removedP, removedO)

            let newGraph = createGraph ()
            let s2 = createUriNode newGraph (Uri "http://example.org/A")
            let p2 = createUriNode newGraph (Uri "http://example.org/name")
            let o2 = createLiteralNode newGraph "Alice" None
            assertTriple newGraph (s2, p2, o2)

            let addedS = createUriNode newGraph (Uri "http://example.org/B")
            let addedP = createUriNode newGraph (Uri "http://example.org/name")
            let addedO = createLiteralNode newGraph "Bob" None
            assertTriple newGraph (addedS, addedP, addedO)

            let diff = DiffEngine.diffGraphs oldGraph newGraph
            Expect.equal diff.Added.Length 1 "Should have 1 added entry"
            Expect.equal diff.Removed.Length 1 "Should have 1 removed entry"

        testCase "detects modification (same subject+predicate, different object)" <| fun _ ->
            let oldGraph = createGraph ()
            let s = createUriNode oldGraph (Uri "http://example.org/X")
            let p = createUriNode oldGraph (Uri "http://example.org/value")
            let oldObj = createLiteralNode oldGraph "old" None
            assertTriple oldGraph (s, p, oldObj)

            let newGraph = createGraph ()
            let s2 = createUriNode newGraph (Uri "http://example.org/X")
            let p2 = createUriNode newGraph (Uri "http://example.org/value")
            let newObj = createLiteralNode newGraph "new" None
            assertTriple newGraph (s2, p2, newObj)

            let diff = DiffEngine.diffGraphs oldGraph newGraph
            Expect.equal diff.Modified.Length 1 "Should have 1 modified entry"
            Expect.equal diff.Added.Length 0 "Should have no pure additions"
            Expect.equal diff.Removed.Length 0 "Should have no pure removals"

        testCase "empty graphs produce no changes" <| fun _ ->
            let g1 = createGraph ()
            let g2 = createGraph ()
            let diff = DiffEngine.diffGraphs g1 g2
            Expect.equal diff.Added.Length 0 "No additions"
            Expect.equal diff.Removed.Length 0 "No removals"
            Expect.equal diff.Modified.Length 0 "No modifications"

        testCase "formatDiff produces readable output" <| fun _ ->
            let diff =
                { Added =
                    [ { Type = "Added"
                        Uri = Uri "http://example.org/A"
                        Label = None
                        Field = Some "http://example.org/name"
                        From = None
                        To = Some "Alice" } ]
                  Removed = []
                  Modified = [] }

            let formatted = DiffEngine.formatDiff diff
            Expect.stringContains formatted "Added:" "Should contain Added section"
            Expect.stringContains formatted "Alice" "Should contain the value"
    ]
