namespace Frank.Rdf

open System

[<Struct>]
[<RequireQualifiedAccess>]
type Node =
    | Iri of string
    | Blank of string

[<RequireQualifiedAccess>]
type Literal =
    | String of string
    | Int of int
    | Bool of bool
    | DateTime of DateTimeOffset
    | LangString of string * string

[<RequireQualifiedAccess>]
type Value =
    | Node of Node
    | Literal of Literal

type Doc =
    { Prefixes: (string * string) list
      Statements: (Node * string * Value) list }

    static member Empty = { Prefixes = []; Statements = [] }

type Description =
    { Subject: Node
      Statements: (string * Value) list }

module Node =
    let blank () : Node = Node.Blank(Guid.NewGuid().ToString())
