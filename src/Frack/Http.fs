namespace Frack

module Http =
  open System
  open System.Collections.Generic
  open System.IO
  open System.Net.Sockets

  let ascii (input:string) = Text.Encoding.ASCII.GetBytes(input)
  let utf8  (input:string) = Text.Encoding.UTF8.GetBytes(input)

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
    let send = ascii >> socket.AsyncSend
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

