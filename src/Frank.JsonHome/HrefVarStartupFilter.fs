namespace Frank.JsonHome

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc.ApiExplorer

exception HrefVarValidationException of messages: string list

[<Sealed>]
type HrefVarStartupFilter(apiDescriptions: IApiDescriptionGroupCollectionProvider) =

    interface IStartupFilter with
        member _.Configure(next: Action<IApplicationBuilder>) : Action<IApplicationBuilder> =
            Action<IApplicationBuilder>(fun app ->
                // Let the rest of the pipeline -- including UseEndpoints --
                // configure first. Only after this call returns does the
                // routing EndpointDataSource (and therefore
                // IApiDescriptionGroupCollectionProvider) reflect the real,
                // final set of resources.
                next.Invoke(app)

                let descriptions =
                    apiDescriptions.ApiDescriptionGroups.Items
                    |> Seq.collect (fun group -> group.Items)

                let failures =
                    ApiSurface.ofApiDescriptions descriptions
                    |> List.collect (fun resource ->
                        let mismatch = HrefVarValidation.diff resource.Href (resource.HrefVars |> List.map fst)

                        [ for name in mismatch.Missing ->
                              $"Resource '{resource.Rel}' ({resource.Href}): route variable '{{{name}}}' has no hrefVar declaration"
                          for name in mismatch.Extra ->
                              $"Resource '{resource.Rel}' ({resource.Href}): hrefVar '{name}' does not match any route template variable" ])

                if not (List.isEmpty failures) then
                    raise (HrefVarValidationException failures))
