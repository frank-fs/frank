namespace Frank.JsonHome

open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =

    type WebHostBuilder with

        /// Serves a JSON Home document and advertises it with a Link header.
        [<CustomOperation("useJsonHome")>]
        member UseJsonHome: spec: WebHostSpec -> WebHostSpec

        [<CustomOperation("useJsonHome")>]
        member UseJsonHome: spec: WebHostSpec * configure: (JsonHomeOptions -> JsonHomeOptions) -> WebHostSpec
