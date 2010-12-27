namespace Frack
open System
open System.Collections.Generic

type Message =
  | Die
  | Req of Owin.IRequest * AsyncReplyChannel<Owin.IResponse>

/// Defines a server application.
type Application(responder, ?timeout) =
  /// Set the default timeout to 110 seconds.
  let timeout = defaultArg timeout 110*60

  /// Create the underlying application agent.
  let agent = MailboxProcessor<_>.Start(fun inbox ->
    let rec loop() = async {
      let! msg = inbox.Receive()
      match msg with
      // Stop processing messages.
      | Die -> return ()
      // Process the request and reply on the response channel.
      | Req(req, resp) ->
          let! response = responder req
          resp.Reply(response)
          return! loop () }
    loop())

  /// Asynchronously runs the app on the request. 
  let runAsync request = async {
    let! response = agent.PostAndAsyncReply((fun resp -> Req(request, resp)), timeout = timeout)
    return response }

  /// Create the begin/end/cancel invoke functions for use implementing Owin.IApplication.
  let beginInvoke, endInvoke, cancelInvoke = Async.AsBeginEnd(runAsync)

  /// Stops the server.
  member this.Stop() = agent.Post(Die)
  /// Runs the application asynchronously.
  member this.AsyncRun(request) = runAsync request
  /// Runs the application synchronously.
  member this.Run(request) =
    agent.PostAndReply((fun resp -> Req(request, resp)), timeout = timeout)

  interface Owin.IApplication with
    /// Begins invocation of the OWIN application.
    member this.BeginInvoke(request, callback, state) =
      beginInvoke(request, callback, state)
    /// Ends invocation of the OWIN application.
    member this.EndInvoke(result) = endInvoke result