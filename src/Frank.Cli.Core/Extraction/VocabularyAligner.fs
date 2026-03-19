namespace Frank.Cli.Core.Extraction

open System
open System.Text.RegularExpressions
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies

/// Aligns extracted RDF to standard vocabularies (Schema.org, Hydra).
module VocabularyAligner =

    let private splitCamelCase (name: string) =
        Regex.Replace(name, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant()

    let private normalizeFieldName (name: string) =
        splitCamelCase(name).Replace(" ", "").ToLowerInvariant()

    let private propertyAlignmentMap: (string list * string) list =
        [ ([ "name"; "title" ], SchemaOrg.Name)
          ([ "description"; "summary"; "body" ], SchemaOrg.Description)
          ([ "email"; "emailaddress" ], SchemaOrg.Email)
          ([ "url"; "uri"; "website"; "homepage" ], SchemaOrg.Url)
          ([ "price"; "cost"; "amount" ], SchemaOrg.Price)
          ([ "createdat"; "datecreated"; "created" ], SchemaOrg.DateCreated)
          ([ "updatedat"; "datemodified"; "modified" ], SchemaOrg.DateModified)
          ([ "image"; "imageurl"; "photo" ], SchemaOrg.Image)
          ([ "telephone"; "phone" ], SchemaOrg.Telephone) ]

    let private classAlignmentMap: (string list * string) list =
        [ ([ "person"; "user"; "customer"; "member"; "employee"; "author"; "contact" ], SchemaOrg.Person)
          ([ "organization"; "company"; "business"; "team"; "group" ], SchemaOrg.Organization)
          ([ "product"; "item"; "goods" ], SchemaOrg.Product)
          ([ "event"; "meeting"; "appointment"; "booking" ], SchemaOrg.Event)
          ([ "place"; "location"; "venue" ], SchemaOrg.Place)
          ([ "creativework"; "post"; "article"; "blog"; "content"; "document"; "page" ], SchemaOrg.CreativeWork)
          ([ "order"; "purchase" ], SchemaOrg.Order)
          ([ "review"; "rating"; "feedback" ], SchemaOrg.Review)
          ([ "offer"; "deal"; "listing" ], SchemaOrg.Offer)
          ([ "mediaobject"; "file"; "attachment"; "media" ], SchemaOrg.MediaObject) ]

    let private tryFindIn (map: (string list * string) list) (name: string) : string option =
        let normalized = normalizeFieldName name

        map
        |> List.tryFind (fun (names, _) -> names |> List.contains normalized)
        |> Option.map snd

    let alignVocabularies (config: TypeMapper.MappingConfig) (graph: IGraph) : IGraph =
        if not (config.Vocabularies |> List.contains "schema.org") then
            graph
        else
            let rdfTypeUri = Uri Rdf.Type
            let datatypePropUri = Uri Owl.DatatypeProperty
            let objectPropUri = Uri Owl.ObjectProperty
            let owlClassUri = Uri Owl.Class
            let equivalentPropUri = Uri Owl.EquivalentProperty
            let equivalentClassUri = Uri Owl.EquivalentClass

            let rdfTypeNode = createUriNode graph rdfTypeUri
            let labelNode = createUriNode graph (Uri Rdfs.Label)

            let typeTriples = triplesWithPredicate graph rdfTypeNode |> Seq.toList

            // Align properties: owl:DatatypeProperty and owl:ObjectProperty → owl:equivalentProperty
            let propertyNodes =
                typeTriples
                |> List.filter (fun t ->
                    match t.Object with
                    | :? IUriNode as on -> on.Uri = datatypePropUri || on.Uri = objectPropUri
                    | _ -> false)
                |> List.map (fun t -> t.Subject)
                |> List.distinct

            for propNode in propertyNodes do
                let labelTriples =
                    triplesWithSubjectPredicate graph propNode labelNode |> Seq.toList

                for labelTriple in labelTriples do
                    match labelTriple.Object with
                    | :? ILiteralNode as lit ->
                        match tryFindIn propertyAlignmentMap lit.Value with
                        | Some schemaUri ->
                            let equivNode = createUriNode graph equivalentPropUri
                            let schemaNode = createUriNode graph (Uri schemaUri)
                            assertTriple graph (propNode, equivNode, schemaNode)
                        | None -> ()
                    | _ -> ()

            // Align classes: owl:Class → owl:equivalentClass
            let classNodes =
                typeTriples
                |> List.filter (fun t ->
                    match t.Object with
                    | :? IUriNode as on -> on.Uri = owlClassUri
                    | _ -> false)
                |> List.map (fun t -> t.Subject)
                |> List.distinct

            for classNode in classNodes do
                let labelTriples =
                    triplesWithSubjectPredicate graph classNode labelNode |> Seq.toList

                for labelTriple in labelTriples do
                    match labelTriple.Object with
                    | :? ILiteralNode as lit ->
                        match tryFindIn classAlignmentMap lit.Value with
                        | Some schemaUri ->
                            let equivNode = createUriNode graph equivalentClassUri
                            let schemaNode = createUriNode graph (Uri schemaUri)
                            assertTriple graph (classNode, equivNode, schemaNode)
                        | None -> ()
                    | _ -> ()

            graph
