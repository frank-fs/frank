namespace Frank.Cli.Core.Extraction

open System
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis

/// Generates SHACL shapes from F# constraints.
module ShapeGenerator =

    let private shapeUri (config: TypeMapper.MappingConfig) (typeName: string) =
        Uri(config.BaseUri.ToString().TrimEnd('/') + "/shapes/" + typeName + "Shape")

    let private classUri (config: TypeMapper.MappingConfig) (typeName: string) =
        Uri(config.BaseUri.ToString().TrimEnd('/') + "/types/" + typeName)

    let private propertyUri (config: TypeMapper.MappingConfig) (typeName: string) (fieldName: string) =
        Uri(config.BaseUri.ToString().TrimEnd('/') + "/properties/" + typeName + "/" + fieldName)

    let rec private fieldKindToRange (config: TypeMapper.MappingConfig) (kind: FieldKind) : Uri * bool =
        match kind with
        | Primitive xsdType ->
            let rangeUri =
                match xsdType with
                | "xsd:string" -> Uri Xsd.String
                | "xsd:integer" -> Uri Xsd.Integer
                | "xsd:long" -> Uri Xsd.Integer
                | "xsd:double" | "xsd:float" -> Uri Xsd.Double
                | "xsd:boolean" -> Uri Xsd.Boolean
                | "xsd:dateTime" -> Uri Xsd.DateTime
                | _ -> Uri Xsd.String
            rangeUri, false
        | Optional inner -> fieldKindToRange config inner
        | Collection element -> fieldKindToRange config element
        | Reference typeName -> classUri config typeName, true

    let private fieldMinCount (kind: FieldKind) : int =
        match kind with
        | Primitive _ -> 1
        | Optional _ -> 0
        | Collection _ -> 0
        | Reference _ -> 1

    let private assertPropertyShape
        (graph: IGraph)
        (config: TypeMapper.MappingConfig)
        (shapeNode: INode)
        (typeName: string)
        (field: AnalyzedField)
        (index: int)
        =
        let blankId = sprintf "ps_%s_%s_%d" typeName field.Name index
        let psNode = createBlankNode graph blankId

        // Link shape to property shape
        assertTriple graph (shapeNode, createUriNode graph (Uri Shacl.Property), psNode)

        // sh:path
        let propUri = propertyUri config typeName field.Name
        assertTriple graph (psNode, createUriNode graph (Uri Shacl.Path), createUriNode graph propUri)

        // sh:datatype or sh:class
        let range, isObj = fieldKindToRange config field.Kind
        if isObj then
            assertTriple graph (psNode, createUriNode graph (Uri Shacl.Class), createUriNode graph range)
        else
            assertTriple graph (psNode, createUriNode graph (Uri Shacl.Datatype), createUriNode graph range)

        // sh:minCount
        let minCount = fieldMinCount field.Kind
        assertTriple graph (
            psNode,
            createUriNode graph (Uri Shacl.MinCount),
            createLiteralNode graph (string minCount) (Some(Uri Xsd.Integer)))

    let private generateShapeForType (graph: IGraph) (config: TypeMapper.MappingConfig) (analyzedType: AnalyzedType) =
        let fields =
            match analyzedType.Kind with
            | Record fields -> [(analyzedType.ShortName, fields)]
            | DiscriminatedUnion cases ->
                // Shape for each case with fields
                cases
                |> List.filter (fun c -> not c.Fields.IsEmpty)
                |> List.map (fun c -> (c.Name, c.Fields))
                |> fun caseShapes ->
                    // Also a shape for the union type itself (no properties)
                    (analyzedType.ShortName, []) :: caseShapes
            | Enum _ -> [(analyzedType.ShortName, [])]

        for (typeName, typeFields) in fields do
            let sUri = shapeUri config typeName
            let sNode = createUriNode graph sUri
            let rdfType = createUriNode graph (Uri Rdf.Type)

            // rdf:type sh:NodeShape
            assertTriple graph (sNode, rdfType, createUriNode graph (Uri Shacl.NodeShape))

            // sh:targetClass
            let cUri = classUri config typeName
            assertTriple graph (sNode, createUriNode graph (Uri Shacl.TargetClass), createUriNode graph cUri)

            // Property shapes
            typeFields |> List.iteri (fun i field ->
                assertPropertyShape graph config sNode typeName field i)

    let generateShapes (config: TypeMapper.MappingConfig) (types: AnalyzedType list) : IGraph =
        let graph = createGraph ()
        for t in types do
            generateShapeForType graph config t
        graph
