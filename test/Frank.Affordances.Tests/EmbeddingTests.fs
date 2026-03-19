module Frank.Affordances.Tests.EmbeddingTests

open System.Reflection
open Expecto

[<Tests>]
let embeddingTests =
    testList
        "Embedding"
        [ testCase "embedded resource is accessible via GetManifestResourceStream"
          <| fun _ ->
              let assembly = Assembly.GetExecutingAssembly()
              use stream = assembly.GetManifestResourceStream("Frank.Affordances.unified-state.bin")
              Expect.isNotNull stream "Embedded resource should exist"
              Expect.isGreaterThan stream.Length 0L "Embedded resource should have content"

          testCase "embedded resource name matches logical name"
          <| fun _ ->
              let assembly = Assembly.GetExecutingAssembly()
              let names = assembly.GetManifestResourceNames()
              Expect.contains names "Frank.Affordances.unified-state.bin" "Resource name should match logical name"

          testCase "embedded resource content matches source file"
          <| fun _ ->
              let assembly = Assembly.GetExecutingAssembly()
              use stream = assembly.GetManifestResourceStream("Frank.Affordances.unified-state.bin")
              let bytes = Array.zeroCreate<byte> (int stream.Length)
              stream.Read(bytes, 0, bytes.Length) |> ignore
              Expect.isGreaterThan bytes.Length 0 "Should have non-empty content" ]
