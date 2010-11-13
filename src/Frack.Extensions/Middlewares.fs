namespace Frack
module Middlewares =
  open Frack

  /// Returns a formatted list of the environment.
  let printEnvironment (app: App) = fun env ->
    let status, hdrs, body = app env
    let vars = seq { for key in env.Keys do yield "\r\n" + key + " => " + (read env.[key]) }
               |> Seq.filter isNotNullOrEmpty
               |> Seq.map ByteString.fromString
               |> Seq.concat
    let bd = seq { yield! body; yield! vars }
    ( status, hdrs, bd )

  /// Intercepts a request using the HEAD method and strips away the returned body from a GET response.
  let head (app:App) = fun (env:Environment) ->
    let status, hdrs, body = app env
    match env?HTTP_METHOD with
    | Str "HEAD" -> (status, hdrs, ByteString.empty)
    | _ -> (status, hdrs, body)
    
  /// Intercepts the environment and checks for use of X_HTTP_METHOD_OVERRIDE.
  let methodOverride (app:App) = fun (env:Environment) ->
    let httpMethods = ["GET";"HEAD";"PUT";"POST";"DELETE";"OPTIONS";"PATCH"]
    let overrideKey = "_method"
    let overrideHeader = "HTTP_X_HTTP_METHOD_OVERRIDE"
    let env' =
      if env?HTTP_METHOD = Str "POST" then
        let form = Request.parseUrlFormEncoded env?input
        let m = if isNotNullOrEmpty form.[overrideKey] then
                  form.[overrideKey]
                else read env.[overrideHeader]
        let httpMethod = m.ToUpperInvariant()
        if httpMethods |> List.exists ((=) httpMethod) then
          seq { yield! env |> Dict.toSeq |> Seq.filter (fun (k,v) -> k <> "HTTP_METHOD")
                yield ("methodoverride_original_method", Str "POST") 
                yield ("HTTP_METHOD", Str httpMethod) } |> dict 
        else env
      else env
    app env'
