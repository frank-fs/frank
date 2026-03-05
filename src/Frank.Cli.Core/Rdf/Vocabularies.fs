namespace Frank.Cli.Core.Rdf

module Vocabularies =
    module Rdf =
        let ns = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"
        let Type = ns + "type"
        let Property = ns + "Property"

    module Rdfs =
        let ns = "http://www.w3.org/2000/01/rdf-schema#"
        let Class = ns + "Class"
        let SubClassOf = ns + "subClassOf"
        let Domain = ns + "domain"
        let Range = ns + "range"
        let Label = ns + "label"
        let Comment = ns + "comment"

    module Owl =
        let ns = "http://www.w3.org/2002/07/owl#"
        let Class = ns + "Class"
        let ObjectProperty = ns + "ObjectProperty"
        let DatatypeProperty = ns + "DatatypeProperty"
        let EquivalentClass = ns + "equivalentClass"
        let EquivalentProperty = ns + "equivalentProperty"
        let Thing = ns + "Thing"

    module Shacl =
        let ns = "http://www.w3.org/ns/shacl#"
        let NodeShape = ns + "NodeShape"
        let PropertyShape = ns + "PropertyShape"
        let TargetClass = ns + "targetClass"
        let Path = ns + "path"
        let Datatype = ns + "datatype"
        let MinCount = ns + "minCount"
        let MaxCount = ns + "maxCount"
        let Class = ns + "class"
        let Property = ns + "property"

    module Hydra =
        let ns = "http://www.w3.org/ns/hydra/core#"
        let Resource = ns + "Resource"
        let Operation = ns + "Operation"
        let SupportedOperation = ns + "supportedOperation"
        let Method = ns + "method"
        let Template = ns + "template"
        let SupportedClass = ns + "supportedClass"
        let ApiDocumentation = ns + "ApiDocumentation"

    module SchemaOrg =
        let ns = "https://schema.org/"
        let Action = ns + "Action"
        let ReadAction = ns + "ReadAction"
        let CreateAction = ns + "CreateAction"
        let UpdateAction = ns + "UpdateAction"
        let DeleteAction = ns + "DeleteAction"
        let Name = ns + "name"
        let Description = ns + "description"
        let Email = ns + "email"
        let Url = ns + "url"
        let Price = ns + "price"
        let DateCreated = ns + "dateCreated"
        let DateModified = ns + "dateModified"

    module Xsd =
        let ns = "http://www.w3.org/2001/XMLSchema#"
        let String = ns + "string"
        let Integer = ns + "integer"
        let Double = ns + "double"
        let Boolean = ns + "boolean"
        let DateTime = ns + "dateTime"
