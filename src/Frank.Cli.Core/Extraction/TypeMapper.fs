namespace Frank.Cli.Core.Extraction

open System
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Extraction.UriHelpers

/// Maps F# types (DUs, records) to OWL classes and properties.
module TypeMapper =

    type MappingConfig =
        { BaseUri: Uri
          Vocabularies: string list }

    type MappedProperty =
        { PropertyUri: Uri
          Label: string
          Domain: Uri
          Range: Uri
          IsObjectProperty: bool }

    type MappedClass =
        { ClassUri: Uri
          Label: string
          Properties: MappedProperty list
          SuperClasses: Uri list }

    let private baseStr (config: MappingConfig) = config.BaseUri.ToString().TrimEnd('/')

    let private mapField (config: MappingConfig) (typeName: string) (field: AnalyzedField) : MappedProperty =
        let b = baseStr config
        let range, isObj = fieldKindToRange b field.Kind

        { PropertyUri = propertyUri b typeName field.Name
          Label = field.Name
          Domain = classUri b typeName
          Range = range
          IsObjectProperty = isObj }

    let private assertProperty (graph: IGraph) (prop: MappedProperty) =
        let propNode = createUriNode graph prop.PropertyUri
        let rdfType = createUriNode graph (Uri Rdf.Type)

        // rdf:type owl:DatatypeProperty or owl:ObjectProperty
        let propTypeUri =
            if prop.IsObjectProperty then
                Uri Owl.ObjectProperty
            else
                Uri Owl.DatatypeProperty

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
        let b = baseStr config

        match analyzedType.Kind with
        | Record fields ->
            let props = fields |> List.map (mapField config analyzedType.ShortName)

            [ { ClassUri = classUri b analyzedType.ShortName
                Label = analyzedType.ShortName
                Properties = props
                SuperClasses = [] } ]
        | DiscriminatedUnion cases ->
            let parentUri = classUri b analyzedType.ShortName

            let parentClass =
                { ClassUri = parentUri
                  Label = analyzedType.ShortName
                  Properties = []
                  SuperClasses = [] }

            let caseClasses =
                cases
                |> List.map (fun case ->
                    let caseProps = case.Fields |> List.map (mapField config case.Name)

                    { ClassUri = classUri b case.Name
                      Label = case.Name
                      Properties = caseProps
                      SuperClasses = [ parentUri ] })

            parentClass :: caseClasses
        | Enum values ->
            [ { ClassUri = classUri b analyzedType.ShortName
                Label = analyzedType.ShortName
                Properties = []
                SuperClasses = [] } ]

    let mapTypes (config: MappingConfig) (types: AnalyzedType list) : IGraph =
        let graph = createGraph ()
        let allClasses = types |> List.collect (mapAnalyzedType config)

        for cls in allClasses do
            assertClass graph cls

        graph
