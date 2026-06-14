module Frank.Cli.MSBuild.Tests.Fixtures

open System
open Frank.Semantic
open Frank.Semantic.LockFile

let confirmedLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Vocabularies =
        Map.ofList
            [ "schema",
              { Uri = "https://schema.org/"
                FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                Hash = "sha256:abc" } ]
      Mappings =
        [ { FSharpType = "TicTacToe.Game"
            Iri = Some "schema:Game"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Fields =
              [ { Name = "identifier"
                  Iri = Some "schema:identifier"
                  Confidence = 1.0
                  Source = Convention
                  Status = Confirmed } ] }
          { FSharpType = "TicTacToe.Move"
            Iri = Some "schema:MoveAction"
            Confidence = 0.9
            Source = Convention
            Status = Confirmed
            Alternates = []
            Fields =
              [ { Name = "rowIndex"
                  Iri = Some "schema:rowIndex"
                  Confidence = 0.8
                  Source = Convention
                  Status = Confirmed } ] } ] }

let proposedLock: LockFile =
    { confirmedLock with
        Mappings =
            [ { FSharpType = "TicTacToe.Game"
                Iri = Some "schema:Game"
                Confidence = 0.7
                Source = Llm
                Status = Proposed
                Alternates = []
                Fields =
                  [ { Name = "identifier"
                      Iri = Some "schema:identifier"
                      Confidence = 0.5
                      Source = Convention
                      Status = Unresolved } ] } ] }

let writeLockFile (dir: string) (lock: LockFile) : string =
    let path = System.IO.Path.Combine(dir, "semantic-mappings.lock.json")
    LockFile.write path lock
    path
