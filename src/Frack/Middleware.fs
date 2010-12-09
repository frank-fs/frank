namespace Frack
module Middleware =
  open Owin

  /// Intercepts a request using the HEAD method and strips away the returned body from a GET response.
  let head (app: IApplication) =
    let asyncInvoke (req: IRequest) = async {
      if req.Method <> "HEAD"
        then return! app.AsyncInvoke(req)
        else let get = Request.Create("GET", req.Uri, req.Headers, req.Items, req.BeginReadBody, req.EndReadBody)
             let! resp = app.AsyncInvoke(get)
             return Response(resp.Status, resp.Headers, (fun () -> Seq.empty)) :> IResponse }
    Application asyncInvoke :> IApplication

  /// Intercepts the environment and checks for use of X_HTTP_METHOD_OVERRIDE.
  let methodOverride (app: IApplication) =
    // Leave out POST, as that is the method we are overriding.
    let httpMethods = ["GET";"HEAD";"PUT";"DELETE";"OPTIONS";"PATCH"]
    let asyncInvoke (req: IRequest) = async { 
      if req.Method <> "POST" ||
         req.Headers?CONTENT_TYPE |> (not << Seq.exists ((=) "application/x-http-form-urlencoded"))
         then return! app.AsyncInvoke(req)
         else
           let! body = req.AsyncReadBody(0)
           let form = ParseFormUrlEncoded body
           let m = if IsNotNullOrEmpty form?_method then form?_method
                   elif not (req.Headers.ContainsKey("HTTP_X_HTTP_METHOD_OVERRIDE")) then
                     req.Headers?HTTP_X_HTTP_METHOD_OVERRIDE |> Seq.head
                   else req.Method
           let httpMethod = m.ToUpperInvariant()
           let req' = if httpMethods |> List.exists ((=) httpMethod)
                        then let items = req.Items
                             items?methodoverride_original_method <- "POST" 
                             Request.Create(httpMethod, req.Uri, req.Headers, items,
                                            (fun (b,o,c) -> async {
                                            // TODO: Find a more efficient way to do this.
                                            System.Array.Copy(body, b, body.Length)
                                            return body.Length }))
                        else req
           return! app.AsyncInvoke(req') }
    Application asyncInvoke :> IApplication