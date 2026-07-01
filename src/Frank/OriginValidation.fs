namespace Frank

open System
open Microsoft.AspNetCore.Http

[<RequireQualifiedAccess>]
module OriginValidation =

    /// Returns Some origin when scheme + "://" + Host.Value is a valid absolute URI,
    /// None when the Host header is malformed.
    let tryValidateOrigin (request: HttpRequest) : string option =
        let origin = request.Scheme + "://" + request.Host.Value
        let mutable uri = Unchecked.defaultof<Uri>

        if Uri.TryCreate(origin, UriKind.Absolute, &uri) then
            Some origin
        else
            None
