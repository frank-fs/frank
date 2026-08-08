namespace Frank.JsonHome

module HrefVarValidation =

    type Mismatch = { Missing: string list; Extra: string list }

    let diff (routeTemplate: string) (declaredNames: string list) : Mismatch =
        let expected = UriTemplate.variables routeTemplate |> Set.ofList
        let declared = declaredNames |> Set.ofList

        { Missing = Set.difference expected declared |> Set.toList |> List.sort
          Extra = Set.difference declared expected |> Set.toList |> List.sort }

    let isValid (mismatch: Mismatch) =
        List.isEmpty mismatch.Missing && List.isEmpty mismatch.Extra
