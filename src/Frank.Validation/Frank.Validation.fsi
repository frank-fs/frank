namespace Frank.Validation

open Frank.Builder

[<AutoOpen>]
module ValidationExtensions =

    type WebHostBuilder with

        [<CustomOperation("useValidationWith")>]
        member UseValidationWith: spec: WebHostSpec * config: ValidationConfig -> WebHostSpec

        [<CustomOperation("useValidation")>]
        member UseValidation: spec: WebHostSpec -> WebHostSpec
