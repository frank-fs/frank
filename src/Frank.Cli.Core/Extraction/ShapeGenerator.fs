namespace Frank.Cli.Core.Extraction

open System
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Extraction.UriHelpers

/// Generates SHACL shapes from F# constraints.
module ShapeGenerator =

    let private baseStr (config: TypeMapper.MappingConfig) = config.BaseUri.ToString().TrimEnd('/')

    let private shapeUri (config: TypeMapper.MappingConfig) (typeName: string) =
        Uri(config.BaseUri.ToString().TrimEnd('/') + "/shapes/" + typeName + "Shape")

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
        let b = baseStr config
        let propUri = propertyUri b typeName field.Name
        assertTriple graph (psNode, createUriNode graph (Uri Shacl.Path), createUriNode graph propUri)

        // sh:datatype or sh:class
        let range, isObj = fieldKindToRange b field.Kind

        if isObj then
            assertTriple graph (psNode, createUriNode graph (Uri Shacl.Class), createUriNode graph range)
        else
            assertTriple graph (psNode, createUriNode graph (Uri Shacl.Datatype), createUriNode graph range)

        // sh:minCount
        let minCount = fieldMinCount field.Kind

        assertTriple
            graph
            (psNode,
             createUriNode graph (Uri Shacl.MinCount),
             createLiteralNode graph (string minCount) (Some(Uri Xsd.Integer)))

    let private generateShapeForType (graph: IGraph) (config: TypeMapper.MappingConfig) (analyzedType: AnalyzedType) =
        let fields =
            match analyzedType.Kind with
            | Record fields -> [ (analyzedType.ShortName, fields) ]
            | DiscriminatedUnion cases ->
                // Shape for each case with fields
                cases
                |> List.filter (fun c -> not c.Fields.IsEmpty)
                |> List.map (fun c -> (c.Name, c.Fields))
                |> fun caseShapes ->
                    // Also a shape for the union type itself (no properties)
                    (analyzedType.ShortName, []) :: caseShapes
            | Enum _ -> [ (analyzedType.ShortName, []) ]

        for (typeName, typeFields) in fields do
            let sUri = shapeUri config typeName
            let sNode = createUriNode graph sUri
            let rdfType = createUriNode graph (Uri Rdf.Type)

            // rdf:type sh:NodeShape
            assertTriple graph (sNode, rdfType, createUriNode graph (Uri Shacl.NodeShape))

            // sh:targetClass
            let cUri = classUri (baseStr config) typeName
            assertTriple graph (sNode, createUriNode graph (Uri Shacl.TargetClass), createUriNode graph cUri)

            // Property shapes
            typeFields
            |> List.iteri (fun i field -> assertPropertyShape graph config sNode typeName field i)

    let generateShapes (config: TypeMapper.MappingConfig) (types: AnalyzedType list) : IGraph =
        let graph = createGraph ()

        for t in types do
            generateShapeForType graph config t

        graph
