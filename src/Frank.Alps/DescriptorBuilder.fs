namespace Frank.Alps

[<AutoOpen>]
module DescriptorBuilderModule =
    [<Sealed>]
    type DescriptorBuilder(id: string) =
        member _.Yield(_) : Descriptor = semantic id
        member _.Zero() : Descriptor = semantic id
        member _.Run(d: Descriptor) : Descriptor = d

        [<CustomOperation("semantic")>]
        member _.Semantic(d: Descriptor) : Descriptor = { d with Type = DescriptorType.Semantic }

        [<CustomOperation("safe")>]
        member _.Safe(d: Descriptor) : Descriptor = { d with Type = DescriptorType.Safe }

        [<CustomOperation("unsafe")>]
        member _.Unsafe(d: Descriptor) : Descriptor = { d with Type = DescriptorType.Unsafe }

        [<CustomOperation("idempotent")>]
        member _.Idempotent(d: Descriptor) : Descriptor = { d with Type = DescriptorType.Idempotent }

        [<CustomOperation("doc")>]
        member _.Doc(d: Descriptor, text: string) : Descriptor = d |> doc text

        [<CustomOperation("docWith")>]
        member _.DocWith(d: Descriptor, doc: Doc) : Descriptor = d |> docWith doc

        [<CustomOperation("def")>]
        member _.Def(d: Descriptor, iri: string) : Descriptor = d |> def iri

        [<CustomOperation("tag")>]
        member _.Tag(d: Descriptor, value: string) : Descriptor = d |> tag value

        [<CustomOperation("rel")>]
        member _.Rel(d: Descriptor, relation: string) : Descriptor = d |> rel relation

        [<CustomOperation("named")>]
        member _.Named(d: Descriptor, name: string) : Descriptor = d |> named name

        [<CustomOperation("ext")>]
        member _.Ext(d: Descriptor, id: string, value: string) : Descriptor = d |> ext id value

        [<CustomOperation("extWith")>]
        member _.ExtWith(d: Descriptor, ext: Ext) : Descriptor = d |> extWith ext

        [<CustomOperation("link")>]
        member _.Link(d: Descriptor, href: string, rel: string) : Descriptor = d |> link href rel

        [<CustomOperation("linkWith")>]
        member _.LinkWith(d: Descriptor, link: Link) : Descriptor = d |> linkWith link

        [<CustomOperation("contains")>]
        member _.Contains(d: Descriptor, children: Descriptor list) : Descriptor = d |> contains children

        [<CustomOperation("rt")>]
        member _.Rt(d: Descriptor, target: Descriptor) : Descriptor = d |> rt target

        [<CustomOperation("href")>]
        member _.Href(d: Descriptor, target: Descriptor) : Descriptor = d |> href target

        [<CustomOperation("hrefExternal")>]
        member _.HrefExternal(d: Descriptor, uri: string) : Descriptor = d |> hrefExternal uri

        [<CustomOperation("initial")>]
        member _.Initial(d: Descriptor) : Descriptor = d |> initial

        [<CustomOperation("regions")>]
        member _.Regions(d: Descriptor, children: Descriptor list) : Descriptor = d |> regions children

        [<CustomOperation("from")>]
        member _.From(d: Descriptor, sources: Descriptor list) : Descriptor = d |> from sources

    let descriptor (id: string) = DescriptorBuilder(id)
