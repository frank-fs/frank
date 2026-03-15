namespace Frank.Cli.Core.Rdf

module Vocabularies =
    module Rdf =
        [<Literal>]
        let ns = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"

        [<Literal>]
        let Type = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"

        [<Literal>]
        let Property = "http://www.w3.org/1999/02/22-rdf-syntax-ns#Property"

    module Rdfs =
        [<Literal>]
        let ns = "http://www.w3.org/2000/01/rdf-schema#"

        [<Literal>]
        let Class = "http://www.w3.org/2000/01/rdf-schema#Class"

        [<Literal>]
        let SubClassOf = "http://www.w3.org/2000/01/rdf-schema#subClassOf"

        [<Literal>]
        let Domain = "http://www.w3.org/2000/01/rdf-schema#domain"

        [<Literal>]
        let Range = "http://www.w3.org/2000/01/rdf-schema#range"

        [<Literal>]
        let Label = "http://www.w3.org/2000/01/rdf-schema#label"

        [<Literal>]
        let Comment = "http://www.w3.org/2000/01/rdf-schema#comment"

    module Owl =
        [<Literal>]
        let ns = "http://www.w3.org/2002/07/owl#"

        [<Literal>]
        let Class = "http://www.w3.org/2002/07/owl#Class"

        [<Literal>]
        let ObjectProperty = "http://www.w3.org/2002/07/owl#ObjectProperty"

        [<Literal>]
        let DatatypeProperty = "http://www.w3.org/2002/07/owl#DatatypeProperty"

        [<Literal>]
        let EquivalentClass = "http://www.w3.org/2002/07/owl#equivalentClass"

        [<Literal>]
        let EquivalentProperty = "http://www.w3.org/2002/07/owl#equivalentProperty"

        [<Literal>]
        let Thing = "http://www.w3.org/2002/07/owl#Thing"

    module Shacl =
        [<Literal>]
        let ns = "http://www.w3.org/ns/shacl#"

        [<Literal>]
        let NodeShape = "http://www.w3.org/ns/shacl#NodeShape"

        [<Literal>]
        let PropertyShape = "http://www.w3.org/ns/shacl#PropertyShape"

        [<Literal>]
        let TargetClass = "http://www.w3.org/ns/shacl#targetClass"

        [<Literal>]
        let Path = "http://www.w3.org/ns/shacl#path"

        [<Literal>]
        let Datatype = "http://www.w3.org/ns/shacl#datatype"

        [<Literal>]
        let MinCount = "http://www.w3.org/ns/shacl#minCount"

        [<Literal>]
        let MaxCount = "http://www.w3.org/ns/shacl#maxCount"

        [<Literal>]
        let Class = "http://www.w3.org/ns/shacl#class"

        [<Literal>]
        let Property = "http://www.w3.org/ns/shacl#property"

        [<Literal>]
        let Pattern = "http://www.w3.org/ns/shacl#pattern"

        [<Literal>]
        let Closed = "http://www.w3.org/ns/shacl#closed"

        [<Literal>]
        let TargetNode = "http://www.w3.org/ns/shacl#targetNode"

        [<Literal>]
        let In = "http://www.w3.org/ns/shacl#in"

        [<Literal>]
        let Or = "http://www.w3.org/ns/shacl#or"

        [<Literal>]
        let Node = "http://www.w3.org/ns/shacl#node"

        [<Literal>]
        let MinInclusive = "http://www.w3.org/ns/shacl#minInclusive"

        [<Literal>]
        let MaxInclusive = "http://www.w3.org/ns/shacl#maxInclusive"

        [<Literal>]
        let MinLength = "http://www.w3.org/ns/shacl#minLength"

        [<Literal>]
        let MaxLength = "http://www.w3.org/ns/shacl#maxLength"

    module Hydra =
        [<Literal>]
        let ns = "http://www.w3.org/ns/hydra/core#"

        [<Literal>]
        let Resource = "http://www.w3.org/ns/hydra/core#Resource"

        [<Literal>]
        let Operation = "http://www.w3.org/ns/hydra/core#Operation"

        [<Literal>]
        let SupportedOperation = "http://www.w3.org/ns/hydra/core#supportedOperation"

        [<Literal>]
        let Method = "http://www.w3.org/ns/hydra/core#method"

        [<Literal>]
        let Template = "http://www.w3.org/ns/hydra/core#template"

        [<Literal>]
        let SupportedClass = "http://www.w3.org/ns/hydra/core#supportedClass"

        [<Literal>]
        let ApiDocumentation = "http://www.w3.org/ns/hydra/core#ApiDocumentation"

    module SchemaOrg =
        [<Literal>]
        let ns = "https://schema.org/"

        [<Literal>]
        let Action = "https://schema.org/Action"

        [<Literal>]
        let ReadAction = "https://schema.org/ReadAction"

        [<Literal>]
        let CreateAction = "https://schema.org/CreateAction"

        [<Literal>]
        let UpdateAction = "https://schema.org/UpdateAction"

        [<Literal>]
        let DeleteAction = "https://schema.org/DeleteAction"

        [<Literal>]
        let Name = "https://schema.org/name"

        [<Literal>]
        let Description = "https://schema.org/description"

        [<Literal>]
        let Email = "https://schema.org/email"

        [<Literal>]
        let Url = "https://schema.org/url"

        [<Literal>]
        let Price = "https://schema.org/price"

        [<Literal>]
        let DateCreated = "https://schema.org/dateCreated"

        [<Literal>]
        let DateModified = "https://schema.org/dateModified"

        [<Literal>]
        let Image = "https://schema.org/image"

        [<Literal>]
        let Telephone = "https://schema.org/telephone"

    module Xsd =
        [<Literal>]
        let ns = "http://www.w3.org/2001/XMLSchema#"

        [<Literal>]
        let String = "http://www.w3.org/2001/XMLSchema#string"

        [<Literal>]
        let Integer = "http://www.w3.org/2001/XMLSchema#integer"

        [<Literal>]
        let Double = "http://www.w3.org/2001/XMLSchema#double"

        [<Literal>]
        let Boolean = "http://www.w3.org/2001/XMLSchema#boolean"

        [<Literal>]
        let DateTime = "http://www.w3.org/2001/XMLSchema#dateTime"

        [<Literal>]
        let Decimal = "http://www.w3.org/2001/XMLSchema#decimal"
