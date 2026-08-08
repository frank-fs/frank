namespace Frank.JsonHome

open Microsoft.AspNetCore.Mvc.ApiExplorer
open Microsoft.Extensions.Options

[<Sealed>]
type internal DuplicateRelValidator(provider: IApiDescriptionGroupCollectionProvider) =
    interface IValidateOptions<JsonHomeOptions> with
        member _.Validate(_name: string, _options: JsonHomeOptions) : ValidateOptionsResult =
            let resources =
                provider.ApiDescriptionGroups.Items
                |> Seq.collect (fun g -> g.Items)
                |> ApiSurface.ofApiDescriptions

            let failures =
                resources
                |> List.groupBy (fun r -> r.Rel)
                |> List.choose (fun (rel, group) ->
                    match group with
                    | []
                    | [ _ ] -> None
                    | duplicates ->
                        let routes =
                            duplicates |> List.map (fun r -> r.Href) |> List.distinct |> String.concat ", "

                        Some $"duplicate JSON Home rel '%s{rel}': %s{routes}")

            match failures with
            | [] -> ValidateOptionsResult.Success
            | fs -> ValidateOptionsResult.Fail(fs: string seq)

[<Sealed>]
type internal FixedJsonHomeOptionsFactory(value: JsonHomeOptions) =
    interface IOptionsFactory<JsonHomeOptions> with
        member _.Create(_name: string) : JsonHomeOptions = value
