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
      Rt: Descriptor option
      From: Descriptor list
      Rel: string option
      Tag: string list
      Link: Link list
      Descriptors: Descriptor list }

and DescriptorRef =
    | Local of Descriptor
    | External of Uri
