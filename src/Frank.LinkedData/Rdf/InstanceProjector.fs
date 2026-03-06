namespace Frank.LinkedData.Rdf

open System
open System.Collections
open System.Collections.Concurrent
open System.Collections.Generic
open System.Reflection
open Microsoft.FSharp.Reflection
open VDS.RDF

/// Projects handler return values to RDF triples.
module InstanceProjector =

    /// Cache: Type -> PropertyInfo[]
    let private propertyCache = ConcurrentDictionary<Type, PropertyInfo[]>()

    /// Cache: ontology graph identity hash -> (lowercase local name -> INode)
    let private ontologyIndexCache =
        ConcurrentDictionary<int, Dictionary<string, INode>>()

    let private getProperties (t: Type) =
        propertyCache.GetOrAdd(t, fun t -> t.GetProperties(BindingFlags.Public ||| BindingFlags.Instance))

    let private getOntologyIndex (ontologyGraph: IGraph) =
        let key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ontologyGraph)

        ontologyIndexCache.GetOrAdd(
            key,
            fun _ ->
                let dict = Dictionary<string, INode>(StringComparer.OrdinalIgnoreCase)

                for triple in ontologyGraph.Triples do
                    match triple.Subject with
                    | :? IUriNode as uriNode ->
                        let uri = uriNode.Uri.ToString()
                        let name = RdfUriHelpers.localName uri

                        if name <> uri then
                            if not (dict.ContainsKey(name)) then
                                dict.[name] <- triple.Subject
                    | _ -> ()

                dict
        )

    let private xsd prefix =
        UriFactory.Root.Create(sprintf "http://www.w3.org/2001/XMLSchema#%s" prefix)

    let rec private addTriples
        (ontologyGraph: IGraph)
        (output: IGraph)
        (subject: INode)
        (instance: obj)
        (instanceType: Type)
        =

        let index = getOntologyIndex ontologyGraph
        let props = getProperties instanceType

        for prop in props do
            let propName = prop.Name

            match index.TryGetValue(propName) with
            | false, _ -> () // No matching ontology property, skip
            | true, ontNode ->
                let predicateUri =
                    match ontNode with
                    | :? IUriNode as u -> u.Uri
                    | _ -> Uri(sprintf "urn:unknown:%s" propName)

                let predicate =
                    output.CreateUriNode(UriFactory.Root.Create(predicateUri.ToString()))

                let value = prop.GetValue(instance)

                if not (isNull value) then
                    projectValue ontologyGraph output subject predicate value (prop.PropertyType)

    and private projectValue
        (ontologyGraph: IGraph)
        (output: IGraph)
        (subject: INode)
        (predicate: INode)
        (value: obj)
        (valueType: Type)
        =

        // Handle FSharpOption
        if
            valueType.IsGenericType
            && valueType.GetGenericTypeDefinition() = typedefof<option<_>>
        then
            let cases = FSharpType.GetUnionCases(valueType)
            let tag = FSharpValue.PreComputeUnionTagReader(valueType)
            let tagVal = tag value
            // None case is tag 0, Some is tag 1
            if tagVal = 1 then // Some
                let someCase = cases |> Array.find (fun c -> c.Tag = 1)
                let fields = someCase.GetFields()

                if fields.Length > 0 then
                    let innerValue = (FSharpValue.GetUnionFields(value, valueType) |> snd).[0]

                    if not (isNull innerValue) then
                        projectValue ontologyGraph output subject predicate innerValue (fields.[0].PropertyType)
        // else None: skip
        // Handle string (before IEnumerable check)
        elif valueType = typeof<string> then
            let literal = output.CreateLiteralNode(value :?> string)
            output.Assert(Triple(subject, predicate, literal)) |> ignore
        // Handle IEnumerable (collections)
        elif typeof<IEnumerable>.IsAssignableFrom(valueType) && valueType <> typeof<string> then
            let enumerable = value :?> IEnumerable

            for item in enumerable do
                if not (isNull item) then
                    projectValue ontologyGraph output subject predicate item (item.GetType())
        // Handle nested F# records
        elif FSharpType.IsRecord(valueType) then
            let blank = output.CreateBlankNode()
            output.Assert(Triple(subject, predicate, blank)) |> ignore
            addTriples ontologyGraph output blank value valueType
        else
            let objectNode =
                match value with
                | :? int as i -> output.CreateLiteralNode(i.ToString(), xsd "integer")
                | :? int64 as i -> output.CreateLiteralNode(i.ToString(), xsd "long")
                | :? float as f -> output.CreateLiteralNode(f.ToString("R"), xsd "double")
                | :? decimal as d -> output.CreateLiteralNode(d.ToString(), xsd "decimal")
                | :? bool as b -> output.CreateLiteralNode((if b then "true" else "false"), xsd "boolean")
                | :? DateTimeOffset as dto -> output.CreateLiteralNode(dto.ToString("o"), xsd "dateTime")
                | :? DateTime as dt -> output.CreateLiteralNode(dt.ToString("o"), xsd "dateTime")
                | _ -> output.CreateLiteralNode(value.ToString())

            output.Assert(Triple(subject, predicate, objectNode)) |> ignore

    /// Projects an object instance to RDF triples using ontology property mappings.
    let project (ontologyGraph: IGraph) (resourceUri: Uri) (instance: obj) : IGraph =
        let output = new Graph()
        let subject = output.CreateUriNode(UriFactory.Root.Create(resourceUri.ToString()))
        let instanceType = instance.GetType()
        addTriples ontologyGraph output subject instance instanceType
        output
