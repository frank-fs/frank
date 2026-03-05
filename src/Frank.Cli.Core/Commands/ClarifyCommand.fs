namespace Frank.Cli.Core.Commands

open System
open System.IO
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.State

/// Ambiguity detection command: identifies unmapped types and routes.
module ClarifyCommand =

    type ClarifyQuestion =
        { Id: string
          Category: string
          QuestionText: string
          Context: {| SourceType: string; Location: string option |}
          Options: {| Label: string; Impact: string |} list }

    type ClarifyResult =
        { Questions: ClarifyQuestion list
          ResolvedCount: int
          TotalCount: int }

    let private slugify (s: string) =
        s.Replace(".", "-").Replace(" ", "-")

    let private unmappedTypeQuestions (state: ExtractionState) : ClarifyQuestion list =
        state.UnmappedTypes
        |> List.map (fun ut ->
            let id = $"unmapped-type-%s{slugify ut.TypeName}"
            { Id = id
              Category = "unmapped-type"
              QuestionText = $"Type '%s{ut.TypeName}' was found but could not be mapped. Should it be:"
              Context =
                {| SourceType = ut.TypeName
                   Location = Some $"%s{ut.Location.File}:%d{ut.Location.Line}" |}
              Options =
                [ {| Label = "mapped as owl:Class"; Impact = "Will generate an OWL class definition" |}
                  {| Label = "ignored"; Impact = "Will be excluded from the ontology" |}
                  {| Label = "mapped as a custom vocabulary term"; Impact = "Will be added with a custom namespace" |} ] })

    let private openOrClosedQuestions (state: ExtractionState) : ClarifyQuestion list =
        // Find classes in the ontology that have subclasses (DU types)
        let graph = state.Ontology
        let rdfTypeNode = createUriNode graph (Uri Rdf.Type)
        let subClassOfNode = createUriNode graph (Uri Rdfs.SubClassOf)
        let owlClassNode = createUriNode graph (Uri Owl.Class)

        // Find all classes (use AbsoluteUri strings since Uri doesn't support comparison)
        let allClassUris =
            triplesWithPredicate graph rdfTypeNode
            |> Seq.filter (fun t ->
                match t.Object with
                | :? IUriNode as on -> on.Uri = Uri Owl.Class
                | _ -> false)
            |> Seq.choose (fun t ->
                match t.Subject with
                | :? IUriNode as sn -> Some sn.Uri.AbsoluteUri
                | _ -> None)
            |> Set.ofSeq

        // Find classes that are superclasses (have subClassOf triples pointing to them)
        let superClasses =
            triplesWithPredicate graph subClassOfNode
            |> Seq.choose (fun t ->
                match t.Object with
                | :? IUriNode as on when allClassUris.Contains on.Uri.AbsoluteUri -> Some on.Uri
                | _ -> None)
            |> Seq.distinctBy (fun (u: Uri) -> u.AbsoluteUri)
            |> Seq.toList

        superClasses
        |> List.map (fun classUri ->
            let typeName = classUri.Segments |> Array.last
            let id = $"open-or-closed-%s{slugify typeName}"
            { Id = id
              Category = "open-or-closed"
              QuestionText = $"Is '%s{typeName}' an open or closed enumeration?"
              Context = {| SourceType = typeName; Location = None |}
              Options =
                [ {| Label = "open (extensible)"; Impact = "New subtypes can be added without breaking compatibility" |}
                  {| Label = "closed (fixed)"; Impact = "The set of subtypes is fixed and complete" |} ] })

    let private objectOrDatatypeQuestions (state: ExtractionState) : ClarifyQuestion list =
        // Find properties with names like URL/email patterns
        let graph = state.Ontology
        let rdfTypeNode = createUriNode graph (Uri Rdf.Type)
        let labelNode = createUriNode graph (Uri Rdfs.Label)
        let domainNode = createUriNode graph (Uri Rdfs.Domain)
        let datatypePropUri = Uri Owl.DatatypeProperty

        let urlLikeNames = Set.ofList ["url"; "uri"; "website"; "homepage"; "email"; "emailaddress"; "link"; "href"]

        // Find datatype properties whose label matches URL-like names
        let datatypeProperties =
            triplesWithPredicate graph rdfTypeNode
            |> Seq.filter (fun t ->
                match t.Object with
                | :? IUriNode as on -> on.Uri = datatypePropUri
                | _ -> false)
            |> Seq.choose (fun t ->
                match t.Subject with
                | :? IUriNode as sn -> Some sn
                | _ -> None)
            |> Seq.toList

        datatypeProperties
        |> List.choose (fun propNode ->
            let labels =
                triplesWithSubjectPredicate graph (propNode :> INode) labelNode
                |> Seq.choose (fun t ->
                    match t.Object with
                    | :? ILiteralNode as lit -> Some lit.Value
                    | _ -> None)
                |> Seq.toList

            let matchingLabel =
                labels
                |> List.tryFind (fun l -> urlLikeNames.Contains(l.ToLowerInvariant()))

            match matchingLabel with
            | None -> None
            | Some fieldName ->
                // Find the domain class
                let domainClass =
                    triplesWithSubjectPredicate graph (propNode :> INode) domainNode
                    |> Seq.choose (fun t ->
                        match t.Object with
                        | :? IUriNode as on -> Some (on.Uri.Segments |> Array.last)
                        | _ -> None)
                    |> Seq.tryHead
                    |> Option.defaultValue "Unknown"

                Some
                    { Id = $"object-or-datatype-%s{slugify fieldName}-%s{slugify domainClass}"
                      Category = "object-or-datatype"
                      QuestionText = $"Should '%s{fieldName}' on '%s{domainClass}' be an object property (link) or datatype property (literal)?"
                      Context = {| SourceType = domainClass; Location = None |}
                      Options =
                        [ {| Label = "object property (link)"; Impact = "Value will be a URI reference to another resource" |}
                          {| Label = "datatype property (literal)"; Impact = "Value will be a string literal (e.g., URL as text)" |} ] })

    let private missingRelationshipQuestions (state: ExtractionState) : ClarifyQuestion list =
        // Detect pairs of classes where one type's name appears in the other's fields
        // but no owl:ObjectProperty links them.
        let graph = state.Ontology
        let rdfTypeNode = createUriNode graph (Uri Rdf.Type)
        let objectPropUri = Uri Owl.ObjectProperty
        let domainNode = createUriNode graph (Uri Rdfs.Domain)
        let rangeNode = createUriNode graph (Uri Rdfs.Range)

        // Collect all class URIs and their short names
        let allClasses =
            triplesWithPredicate graph rdfTypeNode
            |> Seq.filter (fun t ->
                match t.Object with
                | :? IUriNode as on -> on.Uri = Uri Owl.Class
                | _ -> false)
            |> Seq.choose (fun t ->
                match t.Subject with
                | :? IUriNode as sn -> Some (sn.Uri.AbsoluteUri, sn.Uri.Segments |> Array.last)
                | _ -> None)
            |> Seq.toList

        // Collect all object properties and their domain->range pairs
        let objectPropertyLinks =
            triplesWithPredicate graph rdfTypeNode
            |> Seq.filter (fun t ->
                match t.Object with
                | :? IUriNode as on -> on.Uri = objectPropUri
                | _ -> false)
            |> Seq.choose (fun t ->
                match t.Subject with
                | :? IUriNode as sn -> Some sn
                | _ -> None)
            |> Seq.collect (fun propNode ->
                let domains =
                    triplesWithSubjectPredicate graph (propNode :> INode) domainNode
                    |> Seq.choose (fun t ->
                        match t.Object with
                        | :? IUriNode as on -> Some on.Uri.AbsoluteUri
                        | _ -> None)
                let ranges =
                    triplesWithSubjectPredicate graph (propNode :> INode) rangeNode
                    |> Seq.choose (fun t ->
                        match t.Object with
                        | :? IUriNode as on -> Some on.Uri.AbsoluteUri
                        | _ -> None)
                Seq.allPairs domains ranges)
            |> Set.ofSeq

        // For each pair of classes, check if one name appears in the other's rdfs:label fields
        // but no object property links them
        let classUriSet = allClasses |> List.map fst |> Set.ofList

        let questions = ResizeArray<ClarifyQuestion>()
        let seen = System.Collections.Generic.HashSet<string>()

        for (uriA, nameA) in allClasses do
            for (uriB, nameB) in allClasses do
                if uriA <> uriB then
                    let nameAppearsInB =
                        nameA.Length >= 3 && nameB.ToLowerInvariant().Contains(nameA.ToLowerInvariant())
                    if nameAppearsInB then
                        // Check if any object property links A -> B or B -> A
                        let linked =
                            objectPropertyLinks.Contains(uriA, uriB)
                            || objectPropertyLinks.Contains(uriB, uriA)
                        if not linked then
                            let pairKey = if uriA < uriB then $"{nameA}-{nameB}" else $"{nameB}-{nameA}"
                            if seen.Add(pairKey) then
                                questions.Add(
                                    { Id = $"missing-relationship-%s{slugify nameA}-%s{slugify nameB}"
                                      Category = "missing-relationship"
                                      QuestionText = $"Types '%s{nameA}' and '%s{nameB}' appear related but have no explicit property linking them. Should a relationship be added?"
                                      Context = {| SourceType = nameA; Location = None |}
                                      Options =
                                        [ {| Label = $"add object property from %s{nameA} to %s{nameB}"; Impact = $"Will create an owl:ObjectProperty linking %s{nameA} to %s{nameB}" |}
                                          {| Label = $"add object property from %s{nameB} to %s{nameA}"; Impact = $"Will create an owl:ObjectProperty linking %s{nameB} to %s{nameA}" |}
                                          {| Label = "no relationship needed"; Impact = "Types will remain unlinked in the ontology" |} ] })

        questions |> Seq.toList

    let execute (projectPath: string) : Result<ClarifyResult, string> =
        let statePath = ExtractionState.defaultStatePath (Path.GetDirectoryName projectPath)

        match ExtractionState.load statePath with
        | Error e -> Error $"Failed to load state: {e}"
        | Ok state ->

        let unmappedQuestions = unmappedTypeQuestions state
        let openClosedQuestions = openOrClosedQuestions state
        let objDatatypeQuestions = objectOrDatatypeQuestions state
        let missingRelQuestions = missingRelationshipQuestions state

        let allQuestions = unmappedQuestions @ openClosedQuestions @ objDatatypeQuestions @ missingRelQuestions

        let resolvedCount =
            allQuestions
            |> List.filter (fun q -> state.Clarifications.ContainsKey q.Id)
            |> List.length

        Ok
            { Questions = allQuestions
              ResolvedCount = resolvedCount
              TotalCount = allQuestions.Length }
