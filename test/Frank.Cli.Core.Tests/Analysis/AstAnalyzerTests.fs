module Frank.Cli.Core.Tests.Analysis.AstAnalyzerTests

open Expecto
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open Frank.Cli.Core.Analysis

let private checker = FSharpChecker.Create()

let private parseSource (source: string) =
    async {
        let sourceText = SourceText.ofString source
        let parsingOptions = { FSharpParsingOptions.Default with SourceFiles = [| "test.fs" |] }
        let! parseResult = checker.ParseFile("test.fs", sourceText, parsingOptions)
        return parseResult.ParseTree
    }

[<Tests>]
let tests =
    testList "AstAnalyzer" [
        testCaseAsync "single GET resource" <| async {
            let source = """
module Test
let home = resource "/" { get (fun ctx -> task { return () }) }
"""
            let! ast = parseSource source
            let resources = AstAnalyzer.analyzeFile ast
            Expect.equal resources.Length 1 "Should find 1 resource"
            Expect.equal resources.[0].RouteTemplate "/" "Route should be /"
            Expect.equal resources.[0].HttpMethods [Get] "Should have GET method"
            Expect.isNone resources.[0].Name "Should have no name"
            Expect.isFalse resources.[0].HasLinkedData "Should not have linkedData"
        }

        testCaseAsync "multi-method resource with name" <| async {
            let source = """
module Test
let items = resource "/items" {
    name "Items"
    get (fun ctx -> task { return () })
    post (fun ctx -> task { return () })
    delete (fun ctx -> task { return () })
}
"""
            let! ast = parseSource source
            let resources = AstAnalyzer.analyzeFile ast
            Expect.equal resources.Length 1 "Should find 1 resource"
            Expect.equal resources.[0].RouteTemplate "/items" "Route should be /items"
            Expect.equal resources.[0].Name (Some "Items") "Should have name 'Items'"
            Expect.equal resources.[0].HttpMethods.Length 3 "Should have 3 methods"
            Expect.contains resources.[0].HttpMethods Get "Should have GET"
            Expect.contains resources.[0].HttpMethods Post "Should have POST"
            Expect.contains resources.[0].HttpMethods Delete "Should have DELETE"
        }

        testCaseAsync "resource with linkedData" <| async {
            let source = """
module Test
let home = resource "/" {
    get (fun ctx -> task { return () })
    linkedData
}
"""
            let! ast = parseSource source
            let resources = AstAnalyzer.analyzeFile ast
            Expect.equal resources.Length 1 "Should find 1 resource"
            Expect.isTrue resources.[0].HasLinkedData "Should have linkedData"
        }

        testCaseAsync "multiple resources" <| async {
            let source = """
module Test
let home = resource "/" { get (fun ctx -> task { return () }) }
let items = resource "/items" { get (fun ctx -> task { return () }); post (fun ctx -> task { return () }) }
"""
            let! ast = parseSource source
            let resources = AstAnalyzer.analyzeFile ast
            Expect.equal resources.Length 2 "Should find 2 resources"
            Expect.equal resources.[0].RouteTemplate "/" "First route should be /"
            Expect.equal resources.[1].RouteTemplate "/items" "Second route should be /items"
        }

        testCaseAsync "no resources" <| async {
            let source = """
module Test
let x = 42
"""
            let! ast = parseSource source
            let resources = AstAnalyzer.analyzeFile ast
            Expect.equal resources.Length 0 "Should find 0 resources"
        }

        testCaseAsync "analyzeFiles combines results" <| async {
            let source1 = """
module Test1
let home = resource "/" { get (fun ctx -> task { return () }) }
"""
            let source2 = """
module Test2
let items = resource "/items" { post (fun ctx -> task { return () }) }
"""
            let! ast1 = parseSource source1
            let! ast2 = parseSource source2
            let resources = AstAnalyzer.analyzeFiles [ast1; ast2]
            Expect.equal resources.Length 2 "Should find 2 resources across files"
        }
    ]
