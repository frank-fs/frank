namespace Frank.Alps

open System

[<AutoOpen>]
module DescriptorBuilderModule =
    /// Builds a `Descriptor` via computation expression, as an alternative to plain `|>` combinators --
    /// both produce identical `Descriptor` values. Mirrors `Frank.Rdf`'s `DescribeBuilder`/`describe`
    /// exactly: one accumulator, no `Combine`/`Delay`, `Run` returns a plain value.
    [<Sealed>]
    type DescriptorBuilder =
        new: id: string -> DescriptorBuilder
        member inline Yield: 'a -> Descriptor
        member inline Zero: unit -> Descriptor
        member inline Run: d: Descriptor -> Descriptor

        [<CustomOperation("semantic")>]
        member inline Semantic: d: Descriptor -> Descriptor

        [<CustomOperation("safe")>]
        member inline Safe: d: Descriptor -> Descriptor

        [<CustomOperation("unsafe")>]
        member inline Unsafe: d: Descriptor -> Descriptor

        [<CustomOperation("idempotent")>]
        member inline Idempotent: d: Descriptor -> Descriptor

        [<CustomOperation("doc")>]
        member inline Doc: d: Descriptor * text: string -> Descriptor

        [<CustomOperation("docWith")>]
        member inline DocWith: d: Descriptor * doc: Doc -> Descriptor

        [<CustomOperation("def")>]
        member inline Def: d: Descriptor * iri: string -> Descriptor

        [<CustomOperation("tag")>]
        member inline Tag: d: Descriptor * value: string -> Descriptor

        [<CustomOperation("rel")>]
        member inline Rel: d: Descriptor * relation: string -> Descriptor

        [<CustomOperation("named")>]
        member inline Named: d: Descriptor * name: string -> Descriptor

        [<CustomOperation("ext")>]
        member inline Ext: d: Descriptor * id: string * value: string -> Descriptor

        [<CustomOperation("extWith")>]
        member inline ExtWith: d: Descriptor * ext: Ext -> Descriptor

        [<CustomOperation("link")>]
        member inline Link: d: Descriptor * href: string * rel: string -> Descriptor

        [<CustomOperation("linkWith")>]
        member inline LinkWith: d: Descriptor * link: Link -> Descriptor

        [<CustomOperation("contains")>]
        member inline Contains: d: Descriptor * children: Descriptor list -> Descriptor

        [<CustomOperation("rt")>]
        member inline Rt: d: Descriptor * target: Descriptor -> Descriptor

        [<CustomOperation("href")>]
        member inline Href: d: Descriptor * target: Descriptor -> Descriptor

        [<CustomOperation("hrefExternal")>]
        member inline HrefExternal: d: Descriptor * uri: string -> Descriptor

        [<CustomOperation("initial")>]
        member inline Initial: d: Descriptor -> Descriptor

        [<CustomOperation("regions")>]
        member inline Regions: d: Descriptor * children: Descriptor list -> Descriptor

        [<CustomOperation("from")>]
        member inline From: d: Descriptor * sources: Descriptor list -> Descriptor

    /// Enters a `descriptor { }` block: `descriptor "listProducts" { safe; rt product }`.
    val descriptor: id: string -> DescriptorBuilder
