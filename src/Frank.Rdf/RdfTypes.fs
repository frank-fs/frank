namespace Frank.Rdf

open System

[<Struct>]
[<RequireQualifiedAccess>]
type Node =
    | Iri of string
    | Blank of string

[<Struct>]
[<RequireQualifiedAccess>]
type Literal =
    | String of stringValue: string
    | Int of intValue: int
    | Bool of boolValue: bool
    | DateTime of dateTimeValue: DateTimeOffset
    | LangString of text: string * lang: string

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
