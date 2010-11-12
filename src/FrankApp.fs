namespace Frank
[<AutoOpen>]
module FrankApp =
  open System
  open System.Collections.Generic
  open System.Net
  open Microsoft.Http
  open Frack
  open Core
  open Routing

  /// Initializes a Frank application with the specified sequence of routes.
  let init (routes: seq<Route>) =
    // TODO: Subscribe handlers to a request received event instead?
    // TODO: If a given route returns a not found, can you continue, or fall through, to another route?
    // Set a default not found response message.
    let notFound = new HttpResponseMessage(StatusCode = HttpStatusCode.NotFound)

    // Matches the route and request to a registered route.
    let fromRoutes = function Route res -> Some(res) | _ -> None

    // TODO: Move this into a functioning Frack helper.
    let (|/) (split:char) (input:string) =
      if input |> isNotNullOrEmpty then
        let p = input.Split(split) in (p.[0], if p.Length > 1 then p.[1] else "")
      else ("","") // This should never be reached but has to be here to satisfy the return type.
    
    // TODO: Move this into a functioning Frack helper.
    let parseQueryString qs =
      if isNotNullOrEmpty qs then
        let query = qs.Trim('?')
        // Using helpers from Frack.
        query.Split('&') |> Seq.filter isNotNullOrEmpty |> Seq.map ((|/) '=')
      else Seq.empty

    // Executes the handler on the current state of the application.
    let toResponse (request:HttpRequestMessage, handler, parms) =
      // TODO: Pull url-form-encoded values, if any, off the request and stick them in the parms' dictionary.
      let parms' =
        seq {
          yield! parms |> Dict.toSeq
          yield! request.Uri.Query |> parseQueryString
        } |> dict
      run handler parms' (request, notFound)

    // Define the delegate that defines the Frank application. 
    let app = Func<_,_>(fun request ->
      // Find the first matching response. 
      routes
      |> Seq.map (fun route -> (route, request))
      |> Seq.choose fromRoutes
      |> Seq.map toResponse
      |> Seq.head )
    app
