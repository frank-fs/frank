namespace Frank.Cli.Core.Extraction

open System
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis

/// Maps F# types (DUs, records) to OWL classes and properties.
module TypeMapper =

    type MappingConfig = {
        BaseUri: Uri
        Vocabularies: string list
    }

    type MappedProperty = {
        PropertyUri: Uri
        Label: string
        Domain: Uri
        Range: Uri
        IsObjectProperty: bool
    }

    type MappedClass = {
        ClassUri: Uri
        Label: string
        Properties: MappedProperty list
        SuperClasses: Uri list
    }

    let private classUri (config: MappingConfig) (typeName: string) =
        Uri(config.BaseUri.ToString().TrimEnd('/') + "/types/" + typeName)

    let private propertyUri (config: MappingConfig) (typeName: string) (fieldName: string) =
        Uri(config.BaseUri.ToString().TrimEnd('/') + "/properties/" + typeName + "/" + fieldName)

    let rec private fieldKindToRange (config: MappingConfig) (kind: FieldKind) : Uri * bool =
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

    let private mapField (config: MappingConfig) (typeName: string) (field: AnalyzedField) : MappedProperty =
        let range, isObj = fieldKindToRange config field.Kind
        {
            PropertyUri = propertyUri config typeName field.Name
            Label = field.Name
            Domain = classUri config typeName
            Range = range
            IsObjectProperty = isObj
        }

    let private assertProperty (graph: IGraph) (prop: MappedProperty) =
        let propNode = createUriNode graph prop.PropertyUri
        let rdfType = createUriNode graph (Uri Rdf.Type)

        // rdf:type owl:DatatypeProperty or owl:ObjectProperty
        let propTypeUri =
            if prop.IsObjectProperty then Uri Owl.ObjectProperty
            else Uri Owl.DatatypeProperty
        assertTriple graph (propNode, rdfType, createUriNode graph propTypeUri)

        // rdfs:domain
        assertTriple graph (propNode, createUriNode graph (Uri Rdfs.Domain), createUriNode graph prop.Domain)

        // rdfs:range
        assertTriple graph (propNode, createUriNode graph (Uri Rdfs.Range), createUriNode graph prop.Range)

        // rdfs:label
        assertTriple graph (propNode, createUriNode graph (Uri Rdfs.Label), createLiteralNode graph prop.Label None)

    let private assertClass (graph: IGraph) (cls: MappedClass) =
        let classNode = createUriNode graph cls.ClassUri
        let rdfType = createUriNode graph (Uri Rdf.Type)

        // rdf:type owl:Class
        assertTriple graph (classNode, rdfType, createUriNode graph (Uri Owl.Class))

        // rdfs:label
        assertTriple graph (classNode, createUriNode graph (Uri Rdfs.Label), createLiteralNode graph cls.Label None)

        // rdfs:subClassOf
        for superClass in cls.SuperClasses do
            assertTriple graph (classNode, createUriNode graph (Uri Rdfs.SubClassOf), createUriNode graph superClass)

        // Assert all properties
        for prop in cls.Properties do
            assertProperty graph prop

    let private mapAnalyzedType (config: MappingConfig) (analyzedType: AnalyzedType) : MappedClass list =
        match analyzedType.Kind with
        | Record fields ->
            let props = fields |> List.map (mapField config analyzedType.ShortName)
            [{
                ClassUri = classUri config analyzedType.ShortName
                Label = analyzedType.ShortName
                Properties = props
                SuperClasses = []
            }]
        | DiscriminatedUnion cases ->
            let parentUri = classUri config analyzedType.ShortName
            let parentClass = {
                ClassUri = parentUri
                Label = analyzedType.ShortName
                Properties = []
                SuperClasses = []
            }
            let caseClasses =
                cases |> List.map (fun case ->
                    let caseProps = case.Fields |> List.map (mapField config case.Name)
                    {
                        ClassUri = classUri config case.Name
                        Label = case.Name
                        Properties = caseProps
                        SuperClasses = [parentUri]
                    })
            parentClass :: caseClasses
        | Enum values ->
            [{
                ClassUri = classUri config analyzedType.ShortName
                Label = analyzedType.ShortName
                Properties = []
                SuperClasses = []
            }]

    let mapTypes (config: MappingConfig) (types: AnalyzedType list) : IGraph =
        let graph = createGraph ()
        let allClasses = types |> List.collect (mapAnalyzedType config)
        for cls in allClasses do
            assertClass graph cls
        graph
