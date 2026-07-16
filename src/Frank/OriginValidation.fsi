namespace Frank

open Microsoft.AspNetCore.Http

[<RequireQualifiedAccess>]
module OriginValidation =

    /// Returns Some origin when scheme + "://" + Host.Value is a valid absolute URI,
    /// None when the Host header is malformed.
    val tryValidateOrigin: request: HttpRequest -> string option
