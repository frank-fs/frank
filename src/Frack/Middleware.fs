namespace Frack
module Middleware =

  /// Logs the incoming request and the time to respond.
  let log (app: Application) =
    let sw = System.Diagnostics.Stopwatch.StartNew()
    fun (request:Request) -> async {
      let! response = app request
      printfn "Received a %A request from %A. Responded in %i ms." request?METHOD request?SCRIPT_NAME sw.ElapsedMilliseconds
      sw.Reset()
      return response }

  /// Intercepts a request using the HEAD method and strips away the returned body from a GET response.
  let head (app: Application) = fun (request:Request) -> async {
    if (request.["METHOD"] :?> string) <> "HEAD" then return! app request
    else request?METHOD <- "GET"
         let! status, headers, _ = app request
         return (status, headers, seq { yield Array.empty<byte> :> obj }) }

//  /// Intercepts the environment and checks for use of X_HTTP_METHOD_OVERRIDE.
//  let methodOverride (app: IApplication) =
//    // Leave out POST, as that is the method we are overriding.
//    let httpMethods = ["GET";"HEAD";"PUT";"DELETE";"OPTIONS";"PATCH"]
//    let asyncInvoke (req: IRequest) = async { 
//      if req.Method <> "POST" ||
//         req.Headers?CONTENT_TYPE |> (not << Seq.exists ((=) "application/x-http-form-urlencoded"))
//         then return! app.AsyncInvoke(req)
//         else
//           let! body = req.AsyncReadBody(0)
//           let form = parseFormUrlEncoded body
//           let m = if isNotNullOrEmpty form?_method then form?_method
//                   elif not (req.Headers.ContainsKey("HTTP_X_HTTP_METHOD_OVERRIDE")) then
//                     req.Headers?HTTP_X_HTTP_METHOD_OVERRIDE |> Seq.head
//                   else req.Method
//           let httpMethod = m.ToUpperInvariant()
//           let req' = if httpMethods |> List.exists ((=) httpMethod)
//                        then let items = req.Items
//                             items?methodoverride_original_method <- "POST" 
//                             Request.FromAsync(httpMethod, req.Uri, req.Headers, items,
//                                               (fun (b,o,c) -> async {
//                                               // TODO: Find a more efficient way to do this.
//                                               System.Array.Copy(body, b, body.Length)
//                                               return body.Length }))
//                        else req
//           return! app.AsyncInvoke(req') }
//    Application asyncInvoke :> IApplication