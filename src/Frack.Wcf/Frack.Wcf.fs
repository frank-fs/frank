namespace Frack.Hosting
(* License
 *
 * Author: Ryan Riley <ryan.riley@panesofglass.org>
 * Copyright (c) 2011, Ryan Riley.
 *
 * Licensed under the Apache License, Version 2.0.
 * See LICENSE.txt for details.
 *)

[<System.Runtime.CompilerServices.Extension>]
module Wcf =
  open System
  open System.Collections.Generic
  open System.Net
  open System.Net.Http
  open System.ServiceModel
  open System.ServiceModel.Web
  open Frack
  open Frack.Collections

  [<System.Runtime.CompilerServices.Extension>]
  [<Microsoft.FSharp.Core.CompiledName("ToOwinRequest")>]
  let toOwinRequest(request:HttpRequestMessage) =
    // TODO: Consider using the LoadIntoBufferAsync task
    let requestBody = Request.chunk request.Content.ContentReadStream
    let owinRequest = Dictionary<string, obj>() :> IDictionary<string, obj>
    owinRequest.Add("RequestMethod", request.Method)
    owinRequest.Add("RequestUri", request.RequestUri.PathAndQuery)
    request.Headers |> Seq.iter (fun (KeyValue(k, v)) -> owinRequest.Add(k, String.Join(",", v)))
    request.Content.Headers |> Seq.iter (fun (KeyValue(k, v)) -> owinRequest.Add(k, String.Join(",", v)))
    owinRequest.Add("url_scheme", request.RequestUri.Scheme)
    owinRequest.Add("host", request.RequestUri.Host)
    owinRequest.Add("server_port", request.RequestUri.Port)
    owinRequest.Add("RequestBody", requestBody)
    owinRequest

  type HttpRequestMessage with
    member request.ToOwinRequest() = toOwinRequest request


  /// <summary>Creates a new instance of <see cref="Processor"/>.</summary>
  /// <param name="onExecute">The function to execute in the pipeline.</param>
  /// <param name="onGetInArgs">Gets the incoming arguments.</param>
  /// <param name="onGetOutArgs">Gets the outgoing arguments.</param>
  /// <param name="onError">The action to take in the event of a processor error.</param>
  /// <remarks>
  /// This subclass of <see cref="System.ServiceModel.Dispatcher.Processor"/> allows
  /// the developer to create <see cref="System.ServiceModel.Dispatcher.Processor"/>s
  /// using higher-order functions.
  /// </remarks> 
  type Processor(onExecute, ?onGetInArgs, ?onGetOutArgs, ?onError) =
    inherit System.ServiceModel.Dispatcher.Processor()
    let onGetInArgs' = defaultArg onGetInArgs (fun () -> null)
    let onGetOutArgs' = defaultArg onGetOutArgs (fun () -> null)
    let onError' = defaultArg onError ignore
  
    override this.OnGetInArguments() = onGetInArgs'()
    override this.OnGetOutArguments() = onGetOutArgs'()
    override this.OnExecute(input) = onExecute input
    override this.OnError(result) = onError' result


  /// <summary>Creates a new instance of <see cref="FuncConfiguration"/>.</summary>
  /// <param name="requestProcessors">The processors to run when receiving the request.</param>
  /// <param name="responseProcessors">The processors to run when sending the response.</param>
  type FuncConfiguration(?requestProcessors, ?responseProcessors) =
    inherit Microsoft.ServiceModel.Http.HttpHostConfiguration()
    // Set the default values on the optional parameters.
    let requestProcessors' = defaultArg requestProcessors Seq.empty
    let responseProcessors' = defaultArg responseProcessors Seq.empty
  
    // Allows partial application of args to a function using function composition.
    let create args f = f args

    interface Microsoft.ServiceModel.Description.IProcessorProvider with
      member this.RegisterRequestProcessorsForOperation(operation, processors, mode) =
        requestProcessors' |> Seq.iter (processors.Add << (create operation))
      
      member this.RegisterResponseProcessorsForOperation(operation, processors, mode) =
        responseProcessors' |> Seq.iter (processors.Add << (create operation))


  /// <summary>Creates a new instance of <see cref="AppResource"/>.</summary>
  /// <param name="app">The application to invoke.</param>
  /// <remarks>The <see cref="AppResource"/> serves as a catch-all handler for WCF HTTP services.</remarks>
  [<ServiceContract>]
  [<ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)>]
  type AppResource(app: IDictionary<_,_> -> Async<string * IDictionary<string, string> * seq<obj>>) =
    let matchStatus (status:string) =
      let statusParts = status.Split(' ')
      let statusCode = statusParts.[0]
      Enum.Parse(typeof<HttpStatusCode>, statusCode) :?> HttpStatusCode
  
    let handle (request:HttpRequestMessage) (response:HttpResponseMessage) = async {
      let request = request.ToOwinRequest()
      let! status, headers, body = app request
      response.StatusCode <- matchStatus status
      // TODO: Add only response message headers
      headers |> Seq.iter (fun (KeyValue(k,v)) -> response.Headers.Add(k,v))
      response.Content <- new ByteArrayContent(body |> Seq.map (fun o -> o :?> byte[]) |> Array.concat) } |> Async.RunSynchronously
    
    /// <summary>Invokes the application with the specified GET <paramref name="request"/>.</summary>
    /// <param name="request">The <see cref="HttpRequestMessage"/>.</param>
    /// <returns>The <see cref="HttpResponseMessage"/>.</returns>
    /// <remarks>Would like to merge this with the Invoke method, below.</remarks>
    [<OperationContract>]
    [<WebGet(UriTemplate="*")>]
    member x.Get(request, response:HttpResponseMessage) = handle request response
  
    /// <summary>Invokes the application with the specified GET <paramref name="request"/>.</summary>
    /// <param name="request">The <see cref="HttpRequestMessage"/>.</param>
    /// <returns>The <see cref="HttpResponseMessage"/>.</returns>
    [<OperationContract>]
    [<WebInvoke(UriTemplate="*", Method="POST")>]
    member x.Post(request, response:HttpResponseMessage) = handle request response
  
    /// <summary>Invokes the application with the specified GET <paramref name="request"/>.</summary>
    /// <param name="request">The <see cref="HttpRequestMessage"/>.</param>
    /// <returns>The <see cref="HttpResponseMessage"/>.</returns>
    [<OperationContract>]
    [<WebInvoke(UriTemplate="*", Method="PUT")>]
    member x.Put(request, response:HttpResponseMessage) = handle request response
    
    /// <summary>Invokes the application with the specified GET <paramref name="request"/>.</summary>
    /// <param name="request">The <see cref="HttpRequestMessage"/>.</param>
    /// <returns>The <see cref="HttpResponseMessage"/>.</returns>
    [<OperationContract>]
    [<WebInvoke(UriTemplate="*", Method="DELETE")>]
    member x.Delete(request, response:HttpResponseMessage) = handle request response
    
    /// <summary>Invokes the application with the specified GET <paramref name="request"/>.</summary>
    /// <param name="request">The <see cref="HttpRequestMessage"/>.</param>
    /// <returns>The <see cref="HttpResponseMessage"/>.</returns>
    [<OperationContract>]
    [<WebInvoke(UriTemplate="*", Method="*")>]
    member x.Invoke(request, response:HttpResponseMessage) = handle request response
    

  /// <summary>Creates a new instance of <see cref="OwinHost"/>.</summary>
  /// <param name="app">The application to invoke.</param>
  /// <param name="requestProcessors">The processors to run when receiving the request.</param>
  /// <param name="responseProcessors">The processors to run when sending the response.</param>
  /// <param name="baseAddresses">The base addresses to host (defaults to an empty array).</param>
  type OwinHost(app, ?requestProcessors, ?responseProcessors, ?baseAddresses) =
    inherit System.ServiceModel.ServiceHost(AppResource(app), defaultArg baseAddresses [||])
    let requestProcessors = defaultArg requestProcessors Seq.empty
    let responseProcessors = defaultArg responseProcessors Seq.empty
    let baseUris = defaultArg baseAddresses [||]
    let config = new FuncConfiguration(requestProcessors, responseProcessors)
    do for baseUri in baseUris do
         let endpoint = base.AddServiceEndpoint(typeof<AppResource>, new HttpMessageBinding(), baseUri)
         endpoint.Behaviors.Add(new Microsoft.ServiceModel.Description.HttpEndpointBehavior(config))

    /// <summary>Creates a new instance of <see cref="OwinHost"/>.</summary>
    /// <param name="app">The application to invoke.</param>
    /// <param name="requestProcessors">The processors to run when receiving the request.</param>
    /// <param name="responseProcessors">The processors to run when sending the response.</param>
    /// <param name="baseAddresses">The base addresses to host (defaults to an empty array).</param>
    new(app, ?requestProcessors, ?responseProcessors, ?baseAddresses) =
      let baseAddresses = defaultArg baseAddresses [||] |> Array.map (fun baseAddress -> Uri(baseAddress))
      new OwinHost(app, ?requestProcessors = requestProcessors, ?responseProcessors = responseProcessors, baseAddresses = baseAddresses)
    