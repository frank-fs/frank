module Frank.Alps.Tests.HandlerBuilderExtensionsTests

open Microsoft.AspNetCore.Http
open Expecto
open Frank.Builder
open Frank.Alps

[<Tests>]
let tests =
    testList
        "binds"
        [ test "binds attaches a Descriptor retrievable via HandlerDefinition.tryFind" {
              let listProducts = safe "listProducts"

              let def =
                  handler {
                      handle (fun (ctx: HttpContext) -> System.Threading.Tasks.Task.CompletedTask)
                      binds listProducts
                  }

              Expect.equal (HandlerDefinition.tryFind<Descriptor> def) (Some listProducts) ""
          }

          test "a handler without binds has no bound Descriptor" {
              let def =
                  handler { handle (fun (ctx: HttpContext) -> System.Threading.Tasks.Task.CompletedTask) }

              Expect.equal (HandlerDefinition.tryFind<Descriptor> def) None ""
          } ]
