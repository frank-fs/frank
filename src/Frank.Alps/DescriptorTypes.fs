namespace Frank.Alps

open System

[<Struct; RequireQualifiedAccess>]
type DescriptorType =
    | Semantic
    | Safe
    | Unsafe
    | Idempotent

[<Struct>]
type DocFormat =
    | Text
    | Html
    | Asciidoc
    | Markdown

type Doc =
    { Value: string
      Href: Uri option
      Format: DocFormat option
      ContentType: string option
      Tag: string list }

type Link =
    { Href: Uri
      Rel: string
      Title: string option
      Tag: string list }

type Ext =
    { Id: string
      Href: Uri option
      Value: string option
      Tag: string list }

type Descriptor =
    { Id: string
      Name: string option
      Type: DescriptorType
      Def: Uri option
      Doc: Doc option
      Ext: Ext list
      InheritsFrom: DescriptorRef option
      From: Descriptor list
      Guard: StateGuard option
      Rt: Descriptor option
      Targets: TransitionTarget list
      Rel: string option
      Tag: string list
      Link: Link list
      Descriptors: Descriptor list }

and DescriptorRef =
    | Local of Descriptor
    | External of Uri

and StateGuard =
    | State of Descriptor
    | Not of StateGuard
    | All of StateGuard list
    | Any of StateGuard list
    | Predicate of Descriptor

and TransitionTarget =
    | EnterState of Descriptor
    | History of Descriptor
    | DeepHistory of Descriptor
