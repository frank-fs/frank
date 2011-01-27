namespace Frack
module Middleware =
  open System
  open System.Collections.Generic
  open FSharp.Monad

  /// Logs the incoming request and the time to respond.
  let log (app: Action<_,_,_>) =
    Action<IDictionary<string, obj>,_,_>(fun req onCompleted onError ->
      let sw = System.Diagnostics.Stopwatch.StartNew()
      app.Invoke(req, onCompleted, onError)
      printfn "Received a %A request from %A. Responded in %i ms."
              req?RequestMethod req?RequestUri sw.ElapsedMilliseconds
      sw.Reset())

  /// Intercepts a request using the HEAD method and strips away the returned body from a GET response.
  let head (app: Action<_,_,_>) = 
    Action<IDictionary<string, obj>, Action<_,_,_>, _>(fun req onCompleted onError ->
      if (req?RequestMethod :?> string) <> "HEAD" then
        app.Invoke(req, onCompleted, onError)
      else
        req?RequestMethod <- "GET"
        let headHandler = Action<_,_,_>(fun status headers body ->
          onCompleted.Invoke(status, headers, Seq.empty))
        app.Invoke(req, headHandler, onError))

  /// Intercepts a request and checks for use of X_HTTP_METHOD_OVERRIDE.
  let methodOverride (app: Action<_,_,_>) =
    // Leave out POST, as that is the method we are overriding.
    let httpMethods = ["GET";"HEAD";"PUT";"DELETE";"OPTIONS";"PATCH"]
    Action<IDictionary<string, obj>,_,_>(fun req onCompleted onError ->
      let methd = req?RequestMethod :?> string
      let headers = req?RequestHeaders :?> IDictionary<string, string>
      if methd <> "POST" || headers?CONTENT_TYPE <> "application/x-http-form-urlencoded" then
        app.Invoke(req, onCompleted, onError)
      else
        req?RequestBody |> Request.readBody (fun body ->
          let form = UrlEncoded.parseForm body
          let m = if isNotNullOrEmpty form?_method then form?_method
                  elif headers.ContainsKey("HTTP_X_HTTP_METHOD_OVERRIDE") then
                    headers?HTTP_X_HTTP_METHOD_OVERRIDE
                  else methd
          let httpMethod = m.ToUpperInvariant()
          if httpMethods |> List.exists ((=) httpMethod) then
            req?methodoverride_original_method <- "POST" 
            req?RequestMethod <- httpMethod
            req?RequestBody <- Action<Action<_>, Action<_>>(fun onNext onExn ->
              try
                onNext.Invoke(ArraySegment<_>(body))
                onNext.Invoke(ArraySegment<_>(Array.empty))
              with e -> onExn.Invoke(e))
          app.Invoke(req, onCompleted, onError)) onError)