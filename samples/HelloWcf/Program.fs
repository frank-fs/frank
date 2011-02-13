module Main
(* License
 *
 * Author: Ryan Riley <ryan.riley@panesofglass.org>
 * Copyright (c) 2011, Ryan Riley.
 *
 * Licensed under the Apache License, Version 2.0.
 * See LICENSE.txt for details.
 *)

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open Microsoft.ServiceModel.Http
open Frack
open Frack.Collections
open Frack.Hosting.Wcf

let baseurl = "http://localhost:1000/"
let processors = [| (fun op -> new PlainTextProcessor(op, MediaTypeProcessorMode.Response) :> System.ServiceModel.Dispatcher.Processor) |]

let app request = async {
  // do some stuff with the request
  return "200 OK", Dict.empty, "Howdy!"B |> Seq.map (fun b -> b :> obj) }

[<EntryPoint>]
let main(args) =
  let host = OwinHost.FromAsync(app, responseProcessors = processors, baseAddresses = [|baseurl|])
  host.Open()
    
  printfn "Host open.  Hit enter to exit..."
  printfn "Use a web browser and go to %sroot or do it right and get fiddler!" baseurl
    
  System.Console.Read() |> ignore
  host.Close()
  0