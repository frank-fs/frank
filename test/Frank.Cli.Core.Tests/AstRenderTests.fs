module Frank.Cli.Core.Tests.AstRenderTests

open Expecto
open Frank.Cli.Core

[<Tests>]
let astRenderTests =
    testList
        "AstRender"
        [ test "formatTypedValueModule emits namespace + nested module + typed record value" {
              let value =
                  AstRender.recordExpr
                      [ "ProfileUri", AstRender.strExpr "/alps/tictactoe"
                        "AlpsDescriptors",
                        AstRender.listExpr
                            [ AstRender.recordExpr
                                  [ "Id", AstRender.strExpr "MoveAction"
                                    "Type", AstRender.strExpr "semantic"
                                    "Doc", AstRender.noneExpr
                                    "Href", AstRender.someStrExpr "https://schema.org/MoveAction" ] ] ]

              let src =
                  AstRender.formatTypedValueModule
                      "TicTacToe.GeneratedDiscovery"
                      [ "Frank.Discovery" ]
                      "discoveryConfig"
                      "DiscoveryConfig"
                      value

              let expected =
                  "namespace TicTacToe\n\nmodule GeneratedDiscovery =\n    open Frank.Discovery\n\n    let discoveryConfig: DiscoveryConfig =\n        { ProfileUri = \"/alps/tictactoe\"\n          AlpsDescriptors =\n            [ { Id = \"MoveAction\"\n                Type = \"semantic\"\n                Doc = None\n                Href = Some \"https://schema.org/MoveAction\" } ] }\n"

              Expect.equal src expected "byte-exact Fantomas-formatted module"
          }

          test "uriExpr renders Uri applied to a string literal" {
              let src =
                  AstRender.formatTypedValueModule "A.B" [] "x" "System.Uri" (AstRender.uriExpr "https://schema.org/X")

              Expect.stringContains src "let x: System.Uri = Uri \"https://schema.org/X\"" "Uri application"
          }

          test "two calls are byte-identical (determinism)" {
              let mk () =
                  AstRender.formatTypedValueModule "A.B" [] "x" "int" (AstRender.strExpr "1")

              Expect.equal (mk ()) (mk ()) "deterministic output"
          } ]
