namespace Frank.JsonHome

open System.Text.RegularExpressions

module UriTemplate =

    // Matches one {...} segment. Constraint arguments may contain braces-free
    // punctuation such as ':' and '()', so the body is captured up to the
    // closing brace.
    let private segment = Regex(@"\{(?<body>[^{}]*)\}", RegexOptions.Compiled)

    /// Splits a segment body into its catch-all marker and bare variable name,
    /// discarding constraints, optional markers, and default values.
    let private parseBody (body: string) =
        let isCatchAll = body.StartsWith "*"
        let trimmed = body.TrimStart '*'

        // Order matters: a default value may follow a constraint ("{id:int=1}"),
        // and the optional marker trails the name ("{id?}").
        let name =
            let beforeDefault =
                match trimmed.IndexOf '=' with
                | -1 -> trimmed
                | i -> trimmed.Substring(0, i)

            let beforeConstraint =
                match beforeDefault.IndexOf ':' with
                | -1 -> beforeDefault
                | i -> beforeDefault.Substring(0, i)

            beforeConstraint.TrimEnd '?'

        isCatchAll, name

    let ofRouteTemplate (routeTemplate: string) =
        segment.Replace(
            routeTemplate,
            fun m ->
                let isCatchAll, name = parseBody (m.Groups["body"].Value)
                if isCatchAll then "{+" + name + "}" else "{" + name + "}"
        )

    let variables (routeTemplate: string) =
        segment.Matches routeTemplate
        |> Seq.map (fun m -> snd (parseBody (m.Groups["body"].Value)))
        |> List.ofSeq

    let isTemplated (routeTemplate: string) = segment.IsMatch routeTemplate
