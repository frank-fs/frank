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
        member Yield: 'a -> Descriptor
        member Zero: unit -> Descriptor
        member Run: d: Descriptor -> Descriptor

        [<CustomOperation("semantic")>]
        member Semantic: d: Descriptor -> Descriptor

        [<CustomOperation("safe")>]
        member Safe: d: Descriptor -> Descriptor

        [<CustomOperation("unsafe")>]
        member Unsafe: d: Descriptor -> Descriptor

        [<CustomOperation("idempotent")>]
        member Idempotent: d: Descriptor -> Descriptor

        [<CustomOperation("doc")>]
        member Doc: d: Descriptor * text: string -> Descriptor

        [<CustomOperation("docWith")>]
        member DocWith: d: Descriptor * doc: Doc -> Descriptor

        [<CustomOperation("def")>]
        member Def: d: Descriptor * iri: string -> Descriptor

        [<CustomOperation("tag")>]
        member Tag: d: Descriptor * value: string -> Descriptor

        [<CustomOperation("rel")>]
        member Rel: d: Descriptor * relation: string -> Descriptor

        [<CustomOperation("named")>]
        member Named: d: Descriptor * name: string -> Descriptor

        [<CustomOperation("ext")>]
        member Ext: d: Descriptor * id: string * value: string -> Descriptor

        [<CustomOperation("extWith")>]
        member ExtWith: d: Descriptor * ext: Ext -> Descriptor

        [<CustomOperation("link")>]
        member Link: d: Descriptor * href: string * rel: string -> Descriptor

        [<CustomOperation("linkWith")>]
        member LinkWith: d: Descriptor * link: Link -> Descriptor

        [<CustomOperation("contains")>]
        member Contains: d: Descriptor * children: Descriptor list -> Descriptor

        [<CustomOperation("rt")>]
        member Rt: d: Descriptor * target: Descriptor -> Descriptor

        [<CustomOperation("href")>]
        member Href: d: Descriptor * target: Descriptor -> Descriptor

        [<CustomOperation("hrefExternal")>]
        member HrefExternal: d: Descriptor * uri: string -> Descriptor

        [<CustomOperation("initial")>]
        member Initial: d: Descriptor -> Descriptor

        [<CustomOperation("regions")>]
        member Regions: d: Descriptor * children: Descriptor list -> Descriptor

        [<CustomOperation("from")>]
        member From: d: Descriptor * sources: Descriptor list -> Descriptor

    /// Enters a `descriptor { }` block: `descriptor "listProducts" { safe; rt product }`.
    val descriptor: id: string -> DescriptorBuilder
