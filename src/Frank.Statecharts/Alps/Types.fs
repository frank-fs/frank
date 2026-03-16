module internal Frank.Statecharts.Alps.Types

/// Source position for XML parse errors (1-based line and column).
[<Struct>]
type AlpsSourcePosition = { Line: int; Column: int }

/// Parse error with optional source position.
type AlpsParseError =
    { Description: string
      Position: AlpsSourcePosition option }

/// ALPS documentation element (doc).
type AlpsDocumentation =
    { Format: string option
      Value: string }

/// ALPS extension element (ext).
type AlpsExtension =
    { Id: string
      Href: string option
      Value: string option }

/// ALPS link element.
type AlpsLink =
    { Rel: string
      Href: string }

/// ALPS descriptor type discriminated union (FR-003).
type DescriptorType =
    | Semantic
    | Safe
    | Unsafe
    | Idempotent

/// ALPS descriptor -- the core element (self-referential for nesting).
type Descriptor =
    { Id: string option
      Type: DescriptorType
      Href: string option
      ReturnType: string option
      Documentation: AlpsDocumentation option
      Descriptors: Descriptor list
      Extensions: AlpsExtension list
      Links: AlpsLink list }

/// Root ALPS document.
type AlpsDocument =
    { Version: string option
      Documentation: AlpsDocumentation option
      Descriptors: Descriptor list
      Links: AlpsLink list
      Extensions: AlpsExtension list }
