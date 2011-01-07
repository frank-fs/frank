namespace Frack

module Http =
  open System
  open System.Collections.Generic
  open System.IO
  open System.Net.Sockets

  module Parser =
    let ascii (input:string) = Text.Encoding.ASCII.GetBytes(input)
    let utf8  (input:string) = Text.Encoding.UTF8.GetBytes(input)

    let A = 0x41uy
    let B = 0x42uy
    let C = 0x43uy
    let D = 0x44uy
    let E = 0x45uy
    let F = 0x46uy
    let G = 0x47uy
    let H = 0x48uy
    let I = 0x49uy
    let J = 0x4auy
    let K = 0x4buy
    let L = 0x4cuy
    let M = 0x4duy
    let N = 0x4euy
    let O = 0x4fuy
    let P = 0x50uy
    let Q = 0x51uy
    let R = 0x52uy
    let S = 0x53uy
    let T = 0x54uy
    let U = 0x55uy
    let V = 0x56uy
    let W = 0x57uy
    let X = 0x58uy
    let Y = 0x59uy
    let Z = 0x5auy
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

    let HTTP_DELETE = [|D;E;L;E;T;E|]
    let HTTP_GET = [|G;E;T|]
    let HTTP_HEAD = [|H;E;A;D|]
    let HTTP_POST = [|P;O;S;T|]
    let HTTP_PUT = [|P;U;T|]
    let HTTP_CONNECT = [|C;O;N;N;E;C;T|]
    let HTTP_OPTIONS = [|O;P;T;I;O;N;S|]
    let HTTP_TRACE = [|T;R;A;C;E|]
    let HTTP_COPY = [|C;O;P;Y|]
    let HTTP_LOCK = [|L;O;C;K|]
    let HTTP_MKCOL = [|M;K;C;O;L|]
    let HTTP_MOVE = [|M;O;V;E|]
    let HTTP_PROPFIND = [|P;R;O;P;F;I;N;D|]
    let HTTP_PROPPATCH = [|P;R;O;P;P;A;T;C;H|]
    let HTTP_UNLOCK = [|U;N;L;O;C;K|]
    let HTTP_REPORT = [|R;E;P;O;R;T|]
    let HTTP_MKACTIVITY = [|M;K;A;C;T;I;V;I;T;Y|]
    let HTTP_CHECKOUT = [|C;H;E;C;K;O;U;T|]
    let HTTP_MERGE = [|M;E;R;G;E|]

    let CONNECTION = [|C;O;N;N;E;C;T;I;O;N|]
    let PROXY_CONNECTION = [|P;R;O;X;Y;DASH;C;O;N;N;E;C;T;I;O;N|]
    let CONTENT_LENGTH = [|C;O;N;T;E;N;T;DASH;L;E;N;G;T;H|]
    let TRANSFER_ENCODING = [|T;R;A;N;S;F;E;R;DASH;E;N;C;O;D;I;N;G|]
    let UPGRADE = [|U;P;G;R;A;D;E|]
    let CHUNKED = [|C;H;U;N;K;E;D|]
    let KEEP_ALIVE = [|K;E;E;P;DASH;A;L;I;V;E|]
    let CLOSE = [|C;L;O;S;E|]

    let tokens = [|0x00uy..0x7fuy|]

    let UNHEX = [| -1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;
                   -1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;
                   -1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;
                    0; 1; 2; 3; 4; 5; 6; 7; 8; 9;-1;-1;-1;-1;-1;-1;
                   -1;10;11;12;13;14;15;-1;-1;-1;-1;-1;-1;-1;-1;-1;
                   -1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;
                   -1;10;11;12;13;14;15;-1;-1;-1;-1;-1;-1;-1;-1;-1;
                   -1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1;-1; |]

    let UPCASE = [| 0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00; 0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00;
                    0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00; 0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00;
                    0x20;0x00;0x00;0x00;0x00;0x00;0x00;0x00; 0x00;0x00;0x00;0x00;0x00;0x2d;0x00;0x2f;
                    0x30;0x31;0x32;0x33;0x34;0x35;0x36;0x37; 0x38;0x39;0x00;0x00;0x00;0x00;0x00;0x00;
                    0x00;0x41;0x42;0x43;0x44;0x45;0x46;0x47; 0x48;0x49;0x4a;0x4b;0x4c;0x4d;0x4e;0x4f;
                    0x50;0x51;0x52;0x53;0x54;0x55;0x56;0x57; 0x58;0x59;0x5a;0x00;0x00;0x00;0x00;0x5f;
                    0x00;0x41;0x42;0x43;0x44;0x45;0x46;0x47; 0x48;0x49;0x4a;0x4b;0x4c;0x4d;0x4e;0x4f;
                    0x50;0x51;0x52;0x53;0x54;0x55;0x56;0x57; 0x58;0x59;0x5a;0x00;0x00;0x00;0x00;0x00;
                    0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00; 0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00;
                    0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00; 0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00;
                    0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00; 0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00;
                    0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00; 0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00;
                    0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00; 0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00;
                    0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00; 0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00;
                    0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00; 0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00;
                    0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00; 0x00;0x00;0x00;0x00;0x00;0x00;0x00;0x00 |]

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

