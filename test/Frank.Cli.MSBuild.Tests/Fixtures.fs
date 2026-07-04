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
      DeclaredPrefixes = Map.empty
      Mappings =
        [ { FSharpType = "TicTacToe.Game"
            Iri = Some "schema:Game"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
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
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "rowIndex"
                      Iri = Some "schema:rowIndex"
                      Confidence = 0.8
                      Source = Convention
                      Status = Confirmed } ] } ] }

let proposedLock: LockFile =
    { confirmedLock with
        DeclaredPrefixes = Map.empty
        Mappings =
            [ { FSharpType = "TicTacToe.Game"
                Iri = Some "schema:Game"
                Confidence = 0.7
                Source = Llm
                Status = Proposed
                Alternates = []
                Rt = None
                Shape =
                  MappingShape.Record
                      [ { Name = "identifier"
                          Iri = Some "schema:identifier"
                          Confidence = 0.5
                          Source = Convention
                          Status = Unresolved } ] } ] }

/// A lock with a SINGLE type-level Unresolved mapping and ALL FIELDS Confirmed.
/// Used to isolate AT3: the build gate must fail on Unresolved status alone,
/// not only when Proposed is also present.
let unresolvedOnlyLock: LockFile =
    { confirmedLock with
        DeclaredPrefixes = Map.empty
        Mappings =
            [ { FSharpType = "TicTacToe.Game"
                Iri = None
                Confidence = 0.0
                Source = Convention
                Status = Unresolved
                Alternates = []
                Rt = None
                Shape =
                  MappingShape.Record
                      [ { Name = "identifier"
                          Iri = Some "schema:identifier"
                          Confidence = 1.0
                          Source = Convention
                          Status = Confirmed } ] } ] }

/// A fully-confirmed lock that maps types to ex: IRIs (http://example.org/tictactoe#).
/// Used to prove the build pipeline emits ex: IRIs when the vocabulary is swapped.
let confirmedExLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Vocabularies =
        Map.ofList
            [ "ex",
              { Uri = "http://example.org/tictactoe#"
                FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                Hash = "sha256:ex-test" } ]
      DeclaredPrefixes = Map.ofList [ "ex", "http://example.org/tictactoe#" ]
      Mappings =
        [ { FSharpType = "Fixture.Game"
            Iri = Some "ex:Game"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "identifier"
                      Iri = Some "ex:identifier"
                      Confidence = 1.0
                      Source = Convention
                      Status = Confirmed } ] }
          { FSharpType = "Fixture.MoveAction"
            Iri = Some "ex:MoveAction"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "square"
                      Iri = Some "ex:square"
                      Confidence = 1.0
                      Source = Convention
                      Status = Confirmed }
                    { Name = "agent"
                      Iri = Some "ex:agent"
                      Confidence = 1.0
                      Source = Convention
                      Status = Confirmed } ] } ] }

let writeLockFile (dir: string) (lock: LockFile) : string =
    let path = System.IO.Path.Combine(dir, "semantic-mappings.lock.json")
    LockFile.write path lock
    path
