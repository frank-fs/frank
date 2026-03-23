namespace Frank.Cli.Core.Shared

open System.Text.RegularExpressions

/// Shared Schema.org vocabulary alignment functions and data.
/// Used by both VocabularyAligner (RDF graph alignment) and
/// UnifiedAlpsGenerator (ALPS profile generation).
module SchemaAlignment =

    let splitCamelCase (name: string) =
        Regex.Replace(name, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant()

    let normalizeFieldName (name: string) =
        splitCamelCase(name).Replace(" ", "").ToLowerInvariant()

    let propertyAlignmentMap: (string list * string) list =
        [ ([ "name"; "title" ], "https://schema.org/name")
          ([ "description"; "summary"; "body" ], "https://schema.org/description")
          ([ "email"; "emailaddress" ], "https://schema.org/email")
          ([ "url"; "uri"; "website"; "homepage" ], "https://schema.org/url")
          ([ "price"; "cost"; "amount" ], "https://schema.org/price")
          ([ "createdat"; "datecreated"; "created" ], "https://schema.org/dateCreated")
          ([ "updatedat"; "datemodified"; "modified" ], "https://schema.org/dateModified")
          ([ "image"; "imageurl"; "photo" ], "https://schema.org/image")
          ([ "telephone"; "phone" ], "https://schema.org/telephone") ]

    let classAlignmentMap: (string list * string) list =
        [ ([ "person"; "user"; "customer"; "member"; "employee"; "author"; "contact" ], "https://schema.org/Person")
          ([ "organization"; "company"; "business"; "team"; "group" ], "https://schema.org/Organization")
          ([ "product"; "item"; "goods" ], "https://schema.org/Product")
          ([ "event"; "meeting"; "appointment"; "booking" ], "https://schema.org/Event")
          ([ "place"; "location"; "venue" ], "https://schema.org/Place")
          ([ "creativework"; "post"; "article"; "blog"; "content"; "document"; "page" ],
           "https://schema.org/CreativeWork")
          ([ "order"; "purchase" ], "https://schema.org/Order")
          ([ "review"; "rating"; "feedback" ], "https://schema.org/Review")
          ([ "offer"; "deal"; "listing" ], "https://schema.org/Offer")
          ([ "mediaobject"; "file"; "attachment"; "media" ], "https://schema.org/MediaObject") ]

    /// Try to find a Schema.org alignment in the given map for a field/class name.
    let tryFindIn (map: (string list * string) list) (name: string) : string option =
        let normalized = normalizeFieldName name

        map
        |> List.tryFind (fun (names, _) -> names |> List.contains normalized)
        |> Option.map snd

    /// Try to find a Schema.org property alignment for a field name.
    let tryFindPropertyAlignment (fieldName: string) : string option =
        tryFindIn propertyAlignmentMap fieldName

    /// Try to find a Schema.org class alignment for a class name.
    let tryFindClassAlignment (className: string) : string option = tryFindIn classAlignmentMap className
