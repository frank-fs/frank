﻿namespace Frack

module Http =
  open System
  open System.Collections.Generic
  open System.IO
  open System.Net.Sockets

  module Parser =
    let ascii (input:string) = Text.Encoding.ASCII.GetBytes(input)
    let utf8  (input:string) = Text.Encoding.UTF8.GetBytes(input)

    let CR = 0x0duy
    let LF = 0x0auy
    let DOT = 0x2euy
    let SPACE = 0x20uy
    let SEMI = 0x3buy
    let COLON = 0x3auy
    let HASH = 0x23uy
    let QMARK = 0x3fuy
    let SLASH = 0x2fuy
    let DASH = 0x2duy
    let NULL = 0x00uy 

    type HttpMethod =
      | ERROR
      | DELETE
      | GET
      | HEAD
      | POST
      | PUT
      | CONNECT
      | OPTIONS
      | TRACE
      | COPY
      | LOCK
      | MKCOL
      | MOVE
      | PROPFIND
      | PROPPATCH
      | UNLOCK
      | REPORT
      | MKACTIVITY
      | CHECKOUT
      | MERGE

    let CONNECTION = "CONNECTION"B
    let PROXY_CONNECTION = "PROXY-CONNECTION"B
    let CONTENT_LENGTH = "CONTENT-LENGTH"B
    let TRANSFER_ENCODING = "TRANSFER-ENCODING"B
    let UPGRADE = "UPGRADE"B
    let CHUNKED = "CHUNKED"B
    let KEEP_ALIVE = "KEEP-ALIVE"B
    let CLOSE = "CLOSE"B

    let tokens = [|0x00uy..0x7fuy|]

    let UNHEX = [| -1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;
                   -1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;
                   -1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;
                    0; 1; 2; 3; 4; 5; 6; 7; 8; 9;-1;-1;-1;-1;-1;-1;
                   -1;10;11;12;13;14;15;-1;-1;-1;-1;-1;-1;-1;-1;-1;
                   -1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;
                   -1;10;11;12;13;14;15;-1;-1;-1;-1;-1;-1;-1;-1;-1;
                   -1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1 |]

    let UPCASE = [| 0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x20uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x2duy;0x00uy;0x2fuy;
                    0x30uy;0x31uy;0x32uy;0x33uy;0x34uy;0x35uy;0x36uy;0x37uy;
                    0x38uy;0x39uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x41uy;0x42uy;0x43uy;0x44uy;0x45uy;0x46uy;0x47uy;
                    0x48uy;0x49uy;0x4auy;0x4buy;0x4cuy;0x4duy;0x4euy;0x4fuy;
                    0x50uy;0x51uy;0x52uy;0x53uy;0x54uy;0x55uy;0x56uy;0x57uy;
                    0x58uy;0x59uy;0x5auy;0x00uy;0x00uy;0x00uy;0x00uy;0x5fuy;
                    0x00uy;0x41uy;0x42uy;0x43uy;0x44uy;0x45uy;0x46uy;0x47uy;
                    0x48uy;0x49uy;0x4auy;0x4buy;0x4cuy;0x4duy;0x4euy;0x4fuy;
                    0x50uy;0x51uy;0x52uy;0x53uy;0x54uy;0x55uy;0x56uy;0x57uy;
                    0x58uy;0x59uy;0x5auy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;
                    0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy;0x00uy |]

    type HState =
      | General
      | C
      | CO
      | CON

      | MatchingConnection
      | MatchingProxyConnection
      | MatchingContentLength      
      | MatchingTransferEncoding
      | MatchingUpgrade

      | Connection
      | Content_Length
      | Transfer_Encoding
      | Upgrade

      | MatchingTransferEncodingChunked
      | MatchingConnectionKeepAlive
      | MatchingConnectionClose

      | TransferEncodingChunked
      | ConnectionKeepAlive
      | ConnectionClose


  let addRequestLine (scriptName:string) (line:string) (request:Request) =
    let tokens = line.Split([|' '|])
    printfn "Request received from %s" tokens.[1]
    request.Add("REQUEST_METHOD", tokens.[0])
    request.Add("SCRIPT_NAME", if scriptName.StartsWith("/") then scriptName else "/" + scriptName)
    let pathAndQuery = tokens.[1].Split([|'?'|])
    let pathInfo = pathAndQuery.[0].TrimStart('/')
    request.Add("PATH_INFO", if pathInfo.Length > 0 then "/" + pathInfo else pathInfo)
    request.Add("QUERY_STRING", if pathAndQuery.Length > 1 then pathAndQuery.[1] else "")
    request.Add("SERVER_PROTOCOL", tokens.[2])

  let rec addHeaders lastKey (reader:TextReader) (req:Request) =
    let line = reader.ReadLine()
    if line = null || line = "" then ()
    else
      match line with
      | header when line.Contains(":") ->
          let tokens = header.Split([|':'|])
          let key = tokens.[0]
          let value = tokens.[1]
          if req.ContainsKey(key)
            then req.[key] <- req.[key].ToString() + ", " + value 
            else req.Add(key, value)
          addHeaders key reader req
      | value ->
          req.[lastKey] <- req.[lastKey].ToString() + ", " + value
          addHeaders lastKey reader req

  let receive (socket:Socket) (scriptName:string) = async {
    let env = Dictionary<string, obj>() :> Request
    use stream = new NetworkStream(socket, false)
    use inp = new StreamReader(stream)

    env |> addRequestLine scriptName (inp.ReadLine())
    env |> addHeaders "" inp

    // Parse the Host header into SERVER_NAME, SERVER_PORT, and owin.uri_scheme
//    request.Add("SERVER_NAME", uri.Host)
//    request.Add("SERVER_PORT", uri.Port.ToString())
//    request.Add("owin.uri_scheme", uri.Scheme)

    env.Add("owin.input", stream)
    return env }

  let respond (socket:Socket) (response:Response) = async {
    let send = Parser.ascii >> socket.AsyncSend
    let status, headers, body = response
    let bytes = body |> Seq.map (fun o -> o :?> byte[]) |> Seq.concat |> Array.ofSeq
    do! send(sprintf "HTTP/1.1 %s\r\n" status)
    do! send(sprintf "Date: %s\r\n" (DateTime.Now.ToUniversalTime().ToString("R")))
    do! send("Server: Frack/0.8\r\n")
    do! send(sprintf "Content-Length: %d\r\n" bytes.Length)
    for header in headers |> Seq.map (fun (KeyValue(key, value)) -> sprintf "%s: %s\r\n" key value) do
      do! send(header)
    do! send("\r\n")
    do! socket.AsyncSend bytes }

