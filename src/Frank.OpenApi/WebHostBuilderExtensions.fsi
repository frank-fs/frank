namespace Frank.OpenApi

open Microsoft.AspNetCore.OpenApi
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =

    type WebHostBuilder with
        [<CustomOperation("useOpenApi")>]
        member UseOpenApi : spec:WebHostSpec -> WebHostSpec

        [<CustomOperation("useOpenApi")>]
        member UseOpenApi : spec:WebHostSpec * configure:(OpenApiOptions -> unit) -> WebHostSpec
