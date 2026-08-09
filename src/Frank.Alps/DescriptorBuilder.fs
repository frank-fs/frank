namespace Frank.Alps

[<AutoOpen>]
module DescriptorBuilderModule =
    [<Sealed>]
    type DescriptorBuilder(id: string) =
        member _.Yield(_) : Descriptor = semantic id
        member _.Zero() : Descriptor = semantic id
        member inline _.Run(d: Descriptor) : Descriptor = d

        [<CustomOperation("semantic")>]
        member inline _.Semantic(d: Descriptor) : Descriptor = { d with Type = DescriptorType.Semantic }

        [<CustomOperation("safe")>]
        member inline _.Safe(d: Descriptor) : Descriptor = { d with Type = DescriptorType.Safe }

        [<CustomOperation("unsafe")>]
        member inline _.Unsafe(d: Descriptor) : Descriptor = { d with Type = DescriptorType.Unsafe }

        [<CustomOperation("idempotent")>]
        member inline _.Idempotent(d: Descriptor) : Descriptor = { d with Type = DescriptorType.Idempotent }

        [<CustomOperation("doc")>]
        member inline _.Doc(d: Descriptor, text: string) : Descriptor = d |> doc text

        [<CustomOperation("docWith")>]
        member inline _.DocWith(d: Descriptor, doc: Doc) : Descriptor = d |> docWith doc

        [<CustomOperation("def")>]
        member inline _.Def(d: Descriptor, iri: string) : Descriptor = d |> def iri

        [<CustomOperation("tag")>]
        member inline _.Tag(d: Descriptor, value: string) : Descriptor = d |> tag value

        [<CustomOperation("rel")>]
        member inline _.Rel(d: Descriptor, relation: string) : Descriptor = d |> rel relation

        [<CustomOperation("named")>]
        member inline _.Named(d: Descriptor, name: string) : Descriptor = d |> named name

        [<CustomOperation("ext")>]
        member inline _.Ext(d: Descriptor, id: string, value: string) : Descriptor = d |> ext id value

        [<CustomOperation("extWith")>]
        member inline _.ExtWith(d: Descriptor, ext: Ext) : Descriptor = d |> extWith ext

        [<CustomOperation("link")>]
        member inline _.Link(d: Descriptor, href: string, rel: string) : Descriptor = d |> link href rel

        [<CustomOperation("linkWith")>]
        member inline _.LinkWith(d: Descriptor, link: Link) : Descriptor = d |> linkWith link

        [<CustomOperation("contains")>]
        member inline _.Contains(d: Descriptor, children: Descriptor list) : Descriptor = d |> contains children

        [<CustomOperation("rt")>]
        member inline _.Rt(d: Descriptor, target: Descriptor) : Descriptor = d |> rt target

        [<CustomOperation("href")>]
        member inline _.Href(d: Descriptor, target: Descriptor) : Descriptor = d |> href target

        [<CustomOperation("hrefExternal")>]
        member inline _.HrefExternal(d: Descriptor, uri: string) : Descriptor = d |> hrefExternal uri

        [<CustomOperation("initial")>]
        member inline _.Initial(d: Descriptor) : Descriptor = d |> initial

        [<CustomOperation("regions")>]
        member inline _.Regions(d: Descriptor, children: Descriptor list) : Descriptor = d |> regions children

        [<CustomOperation("from")>]
        member inline _.From(d: Descriptor, sources: Descriptor list) : Descriptor = d |> from sources

    let descriptor (id: string) = DescriptorBuilder(id)
