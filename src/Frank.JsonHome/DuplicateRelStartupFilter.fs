namespace Frank.JsonHome

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc.ApiExplorer
open Microsoft.Extensions.Options

[<Sealed>]
type internal DuplicateRelStartupFilter(provider: IApiDescriptionGroupCollectionProvider) =
    interface IStartupFilter with
        member _.Configure(next: Action<IApplicationBuilder>) : Action<IApplicationBuilder> =
            Action<IApplicationBuilder>(fun app ->
                next.Invoke(app)

                let resources =
                    provider.ApiDescriptionGroups.Items
                    |> Seq.collect (fun g -> g.Items)
                    |> ApiSurface.ofApiDescriptions

                let failures =
                    resources
                    |> List.groupBy (fun r -> r.Rel)
                    |> List.choose (fun (rel, group) ->
                        match group with
                        | [ _ ] -> None
                        | duplicates ->
                            let routes =
                                duplicates |> List.map (fun r -> r.Href) |> List.distinct |> String.concat ", "

                            Some $"duplicate JSON Home rel '%s{rel}': %s{routes}")

                match failures with
                | [] -> ()
                | fs -> raise (OptionsValidationException("JsonHome", typeof<JsonHomeOptions>, fs)))
