namespace Frank
open System
open System.Collections.Generic
open System.Net
open Microsoft.Http
open FSharp.Monad
open Frack

// TODO: If a given route returns a not found, can you continue, or fall through, to another route?

/// A Frank application with the specified sequence of routes.
type App (routes:seq<Route>, ?before:Handler, ?after:Handler, ?formatters:seq<Formatter>) =
  let before' = defaultArg before State.empty
  let after' = defaultArg after State.empty
  let formatters' = defaultArg formatters Seq.empty

  // Set a default not found response message.
  let notFound = new HttpResponseMessage(StatusCode = HttpStatusCode.NotFound)

  // Matches the route and request to a registered route.
  let fromRoutes = function Route res -> Some(res) | _ -> None

  // Collect the passed in parameters, as well as query string and form-urlencoded parameters.
  let collectParams parms (uri:Uri) (content:HttpContent) = 
    seq {
      yield! parms |> Dict.toSeq
      yield! uri.Query |> Request.parseQueryString |> Dict.toSeq
      if content <> null && content.ContentType = "application/x-http-form-urlencoded" then
        yield! content.ReadAsByteArray() |> Request.parseFormUrlEncoded |> Dict.toSeq
    } |> dict

  // Executes the handler on the current state of the application.
  let toResponse (request:HttpRequestMessage, handler, parms) =
    let parms' = collectParams parms request.Uri request.Content
    let handler' = frank {
      do! before'
      do! handler
      do! after'
      return! getResponse }
    State.eval handler' (request, notFound, parms', formatters')

  let requestReceivedEvent = new Event<HttpRequestMessage>()

  let responseSentEvent = new Event<HttpResponseMessage>()

  [<CLIEvent>]
  member this.RequestReceived = requestReceivedEvent.Publish

  [<CLIEvent>]
  member this.ResponseSent = responseSentEvent.Publish

  /// Inokes the Frank app on the specified request.
  member this.Invoke(request) =
    requestReceivedEvent.Trigger(request)
    // Find the first matching response.
    let response = 
      routes |> Seq.map (fun route -> (route, request))
             |> Seq.choose fromRoutes
             |> Seq.map toResponse
             |> Seq.head
    responseSentEvent.Trigger(response)
    response

module FrankApp =
  /// Converts a Frank application into a Frack application.
  let toFrack (app:App) = fun env ->
    env |> Request.fromFrack
        |> app.Invoke
        |> Response.toFrack
