namespace Frack
open System
open System.Collections.Generic
open Owin

type Message =
  | Req of IRequest
  | Resp of AsyncReplyChannel<IResponse option>

type Application(responder, ?timeout) =
  // Set the default timeout to 110 seconds.
  let timeout = defaultArg timeout 110*60
  // Create the underlying application agent.
  let agent = MailboxProcessor<_>.Start(fun inbox ->
    let rec loop request = async {
      let! msg = inbox.Receive()
      match msg with
      // Set the request for the response to process.
      | Req req -> return! loop(Some(req))
      | Resp resp ->
          match request with
          // If no request has been set, return None.
          | None -> resp.Reply(None)
          // If a request was provided, process the response then send the result.
          | Some(req) ->
              let! response = responder req
              resp.Reply(Some(response))
          // Restore the state to that of having no request.
          return! loop None }
    // Start the loop with a current request state of None.
    // If a response is requested before a request is received,
    // the caller will get nothing back.
    loop None)

  let headers = Dictionary<string, seq<string>>() :> IDictionary<string, seq<string>>
  let emptyResponse = Response("404 Not Found", headers, null) :> IResponse

  let runAsync request = async {
    agent.Post(Req request)
    let! response = agent.PostAndAsyncReply(Resp, timeout = timeout)
    return defaultArg response emptyResponse }

  let beginInvoke, endInvoke, cancelInvoke = Async.AsBeginEnd(runAsync)

  member this.RunAsync(request) = runAsync request

  member this.Run(request) =
    agent.Post(Req request)
    let response = agent.PostAndReply(Resp, timeout = timeout)
    defaultArg response emptyResponse

  interface IApplication with
    member this.BeginInvoke(request, callback, state) =
      beginInvoke(request, callback, state)
    member this.EndInvoke(result) = endInvoke result