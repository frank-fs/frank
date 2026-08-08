namespace Frank.Alps

open System

/// The four ALPS descriptor kinds (draft-07 §2.2.16). Struct: data-free, no allocation.
[<Struct; RequireQualifiedAccess>]
type DescriptorType =
    | Semantic
    | Safe
    | Unsafe
    | Idempotent

/// Values of `doc`'s `format` attribute (draft-07 §2.2.5). Struct: data-free, no allocation.
[<Struct>]
type DocFormat =
    | Text
    | Html
    | Asciidoc
    | Markdown

/// A descriptor's `doc` element: free-form documentation text plus optional href/format/contentType/tag.
type Doc =
    { Value: string
      Href: Uri option
      Format: DocFormat option
      ContentType: string option
      Tag: string list }

/// An RFC 8288 web link on a descriptor -- distinct from a descriptor's own `href` (inheritance).
type Link =
    { Href: Uri
      Rel: string
      Title: string option
      Tag: string list }

/// A descriptor's `ext` element: author-specific extension data (draft-07 §2.2.6).
type Ext =
    { Id: string
      Href: Uri option
      Value: string option
      Tag: string list }

/// One ALPS descriptor. Self-referential: `Rt`, `Descriptors`, `From`, and (via `DescriptorRef`)
/// `InheritsFrom` all hold other `Descriptor` values directly, not string ids -- dangling references
/// are compile errors, not runtime failures. Deliberately not `[<Struct>]`: an 11-field record threaded
/// through every combinator and CE step would mean copying the whole record at each pipe step rather
/// than passing one reference (design doc, `[<Struct>]` section).
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

/// Where a descriptor's `href` (inheritance) points: a value in this process, or a URI into a
/// document this codebase does not own (nothing to check against, so a bare Uri).
and DescriptorRef =
    | Local of Descriptor
    | External of Uri

/// A structural guard tree over descriptor state, evaluated against a resolver's active-state set.
/// `State`/`Predicate` hold a plain, local Descriptor -- cross-role composition connects via a shared
/// `Def` URI on independently-authored descriptors, not via a cross-document reference (design doc,
/// 2026-08-08-frank-alps-compound-transitions-design.md).
and StateGuard =
    | State of Descriptor
    | Not of StateGuard
    | All of StateGuard list
    | Any of StateGuard list
    | Predicate of Descriptor

/// Where a fan-out transition enters. Always local to the same document as the transition -- a
/// transition can only enter its own document's orthogonal regions.
and TransitionTarget =
    | EnterState of Descriptor
    | History of Descriptor
    | DeepHistory of Descriptor
