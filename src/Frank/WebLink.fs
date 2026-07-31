namespace Frank.Builder

type WebLink =
    { Target: string
      Rel: string
      Params: (string * string) list }

module WebLink =

    let private escapeParam (value: string) =
        value.Replace("\\", "\\\\").Replace("\"", "\\\"")

    let format (link: WebLink) : string =
        let paramStr =
            link.Params
            |> List.map (fun (name, value) -> "; " + name + "=\"" + escapeParam value + "\"")
            |> String.concat ""

        "<" + link.Target + ">; rel=\"" + escapeParam link.Rel + "\"" + paramStr
