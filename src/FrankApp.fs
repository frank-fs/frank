namespace Frank
module FrankApp =
  open System
  open System.Collections.Generic
  open System.Net
  open Microsoft.Http
  open Frack

  /// Initializes a Frank application with the specified sequence of routes.
  let init (routes: seq<Route>) =
    // TODO: Subscribe handlers to a request received event instead?
    // TODO: If a given route returns a not found, can you continue, or fall through, to another route?
    // Set a default not found response message.
    let notFound = new HttpResponseMessage(StatusCode = HttpStatusCode.NotFound)

    // Matches the route and request to a registered route.
    let fromRoutes = function Route res -> Some(res) | _ -> None

    // Executes the handler on the current state of the application.
    let toResponse (request:HttpRequestMessage, handler, parms) =
      // Collect the passed in parameters, as well as query string and form-urlencoded parameters.
      let parms' =
        seq {
          yield! parms |> Dict.toSeq
          yield! request.Uri.Query |> Request.parseQueryString |> Dict.toSeq
          if request.Content <> null && request.Content.ContentType = "application/x-http-form-urlencoded" then
            yield! request.Content.ReadAsByteArray() |> Request.parseFormUrlEncoded |> Dict.toSeq
        } |> dict
      // Add the parameter dictionary to the request properties.
      request.Properties.Add(parms')
      // Eval the handler with the initial state of the request and a not found response.
      // This returns an HttpResponseMessage.
      run handler (request, notFound)

    // Define the delegate that defines the Frank application. 
    let app = Func<_,_>(fun request ->
      // Find the first matching response. 
      routes
      |> Seq.map (fun route -> (route, request))
      |> Seq.choose fromRoutes
      |> Seq.map toResponse
      |> Seq.head )
    app

  /// Converts a Frank application into a Frack application.
  let toFrack (app:Frank.App) = fun env ->
    env |> Request.fromFrack
        |> app.Invoke
        |> Response.toFrack
