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

    let private unmappedTypeQuestions (state: ExtractionState) : ClarifyQuestion list =
        state.UnmappedTypes
        |> List.mapi (fun i ut ->
            let id = $"unmapped-type-%d{i}"
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
        |> List.mapi (fun i classUri ->
            let typeName = classUri.Segments |> Array.last
            let id = $"open-or-closed-%d{i}"
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
                    { Id = $"object-or-datatype-%s{fieldName}-%s{domainClass}"
                      Category = "object-or-datatype"
                      QuestionText = $"Should '%s{fieldName}' on '%s{domainClass}' be an object property (link) or datatype property (literal)?"
                      Context = {| SourceType = domainClass; Location = None |}
                      Options =
                        [ {| Label = "object property (link)"; Impact = "Value will be a URI reference to another resource" |}
                          {| Label = "datatype property (literal)"; Impact = "Value will be a string literal (e.g., URL as text)" |} ] })

    let execute (projectPath: string) : Result<ClarifyResult, string> =
        let statePath = ExtractionState.defaultStatePath (Path.GetDirectoryName projectPath)

        match ExtractionState.load statePath with
        | Error e -> Error $"Failed to load state: {e}"
        | Ok state ->

        let unmappedQuestions = unmappedTypeQuestions state
        let openClosedQuestions = openOrClosedQuestions state
        let objDatatypeQuestions = objectOrDatatypeQuestions state

        let allQuestions = unmappedQuestions @ openClosedQuestions @ objDatatypeQuestions

        let resolvedCount =
            allQuestions
            |> List.filter (fun q -> state.Clarifications.ContainsKey q.Id)
            |> List.length

        Ok
            { Questions = allQuestions
              ResolvedCount = resolvedCount
              TotalCount = allQuestions.Length }
