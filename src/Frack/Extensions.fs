namespace Frack

//[<AutoOpen>]
//[<System.Runtime.CompilerServices.Extension>]
//module Extensions =
//  // Owin.IRequest Extensions    
//  [<System.Runtime.CompilerServices.Extension>]
//  let Path(request:Owin.IRequest) = request.Uri |> (SplitUri >> fst)
//
//  [<System.Runtime.CompilerServices.Extension>]
//  let QueryString(request:Owin.IRequest) = request.Uri |> (SplitUri >> snd)
//
//  [<System.Runtime.CompilerServices.Extension>]
//  let AsyncReadBody(request:Owin.IRequest, buffer, offset, count) =
//    Async.FromBeginEnd(buffer, offset, count, request.BeginReadBody, request.EndReadBody)
//
//  [<System.Runtime.CompilerServices.Extension>]
//  let AsyncReadBodyToEnd(request:Owin.IRequest, count) =
//    let asyncReadBody = (fun (buffer, offset, count) ->
//      Async.FromBeginEnd(buffer, offset, count, request.BeginReadBody, request.EndReadBody))
//    let readBodyToEnd(count) = async {
//      let notFinished = ref true
//      let initialSize = if count > 1 then count else (2 <<< 16)
//      let buffer = Array.zeroCreate<byte> initialSize
//      use ms = new System.IO.MemoryStream()
//      while !notFinished do
//        let! read = asyncReadBody(buffer, 0, buffer.Length)
//        if read <= 0 then notFinished := false
//        ms.Write(buffer, 0, read)
//      return ms.ToArray() }
//    readBodyToEnd(count)
//
//  [<System.Runtime.CompilerServices.Extension>]
//  let AsyncReadAsString(request:Owin.IRequest) = async {
//    let! bytes = AsyncReadBodyToEnd(request, 2 <<< 16)
//    return System.Text.Encoding.UTF8.GetString(bytes) }
//
//  [<System.Runtime.CompilerServices.Extension>]
//  let ReadAsString(request:Owin.IRequest) = AsyncReadAsString request |> Async.RunSynchronously
//
//  [<System.Runtime.CompilerServices.Extension>]
//  let ReadBody(request:Owin.IRequest, buffer, offset, count) =
//    Async.FromBeginEnd(buffer, offset, count, request.BeginReadBody, request.EndReadBody)
//    |> Async.RunSynchronously
//
//  type Owin.IRequest with
//    /// Gets the Uri path.
//    member this.Path = Path this
//
//    /// Gets the query string.
//    member this.QueryString = QueryString this
//
//    /// <summary>Reads the HTTP request body asynchronously.</summary>
//    /// <param name="buffer">The byte buffer.</param>
//    /// <param name="offset">The offset at which to start reading.</param>
//    /// <param name="count">The number of bytes to read.</param>
//    /// <returns>An <see cref="Async{T}"/> computation returning the number of bytes read.</returns>
//    member this.AsyncReadBody(buffer, offset, count) = AsyncReadBody(this, buffer, offset, count)
//
//    /// <summary>Reads the HTTP request body asynchronously.</summary>
//    /// <param name="count">The number of bytes to read.</param>
//    /// <returns>An <see cref="Async{T}"/> computation returning the bytes read.</returns>
//    /// <remarks>If the <paramref name="count"/> is less than 1, the buffer size is set to 32768.</remarks>
//    member this.AsyncReadBody(count) = AsyncReadBodyToEnd(this, count)
//
//    member this.AsyncReadAsString() = AsyncReadAsString this
//
//    member this.ReadAsString() = ReadAsString this
//
//    /// <summary>Reads the HTTP request body synchronously.</summary>
//    /// <param name="buffer">The byte buffer.</param>
//    /// <param name="offset">The offset at which to start reading.</param>
//    /// <param name="count">The number of bytes to read.</param>
//    /// <returns>The number of bytes read.</returns>
//    member this.ReadBody(buffer, offset, count) = ReadBody(this, buffer, offset, count)
//
//  // Owin.IResponse Extensions
//  [<System.Runtime.CompilerServices.Extension>]
//  let StatusCode(response:Owin.IResponse) = response.Status |> (fst << splitStatus) 
//
//  [<System.Runtime.CompilerServices.Extension>]
//  let StatusDescription(response:Owin.IResponse) = response.Status |> (snd << splitStatus) 
//
//  let rec writeTo stream item =
//    match item with
//    // Matches and iterates a sequence recursively to the stream
//    | Sequence it -> it |> Seq.iter (writeTo stream)
//    // Transfers the bytes to the stream
//    | Bytes bs -> bs |> ByteString.transfer stream
//    // Converts a FileInfo into a SeqStream, then transfers the bytes to the stream
//    | File fi -> fi |> ByteString.fromFileInfo |> ByteString.transfer stream
//    // Converts a string into a SeqStream, then transfers the bytes to the stream
//    | Str str -> str |> ByteString.fromString |> ByteString.transfer stream
//    // Ignore until I better understand ArraySegment
//    | Segment seg -> ()
//
//  [<System.Runtime.CompilerServices.Extension>]
//  let WriteToStream(response:Owin.IResponse, stream) = response.GetBody() |> Seq.iter (writeTo stream)
//
//  type Owin.IResponse with
//    member this.StatusCode = StatusCode this
//    member this.StatusDescription = StatusDescription this
//    member this.WriteToStream stream = this.GetBody() |> Seq.iter (writeTo stream)
//
//  // Owin.IApplication Extensions
//  [<System.Runtime.CompilerServices.Extension>]
//  let AsyncInvoke(app:Owin.IApplication, request) =
//    Async.FromBeginEnd(request, app.BeginInvoke, app.EndInvoke)
//
//  [<System.Runtime.CompilerServices.Extension>]
//  let Invoke(app:Owin.IApplication, request) =
//    Async.FromBeginEnd(request, app.BeginInvoke, app.EndInvoke)
//    |> Async.RunSynchronously
//
//  type Owin.IApplication with
//    /// <summary>Invokes the application asynchronously.</summary>
//    /// <param name="request">The HTTP <see cref="Owin.IRequest"/>.</param>
//    /// <returns>An <see cref="Async{T}"/> computation returning an <see cref="Owin.IResponse"/>.</returns>
//    member this.AsyncInvoke(request) = Async.FromBeginEnd(request, this.BeginInvoke, this.EndInvoke)
//
//    /// <summary>Invokes the application synchronously.</summary>
//    /// <param name="request">The HTTP <see cref="Owin.IRequest"/>.</param>
//    /// <returns>An <see cref="Owin.IResponse"/>.</returns>
//    member this.Invoke(request) = Async.FromBeginEnd(request, this.BeginInvoke, this.EndInvoke)
//                                  |> Async.RunSynchronously
//
//  /// Extends NameValueCollection with methods to transform it to an enumerable.
//  [<System.Runtime.CompilerServices.Extension>]
//  let AsEnumerable(this:System.Collections.Specialized.NameValueCollection) =
//    seq { for key in this.Keys do yield (key, this.[key]) }
//
//  /// Extends NameValueCollection with methods to transform it to a dictionary.
//  [<System.Runtime.CompilerServices.Extension>]
//  let ToDictionary(this:System.Collections.Specialized.NameValueCollection) = this |> AsEnumerable |> dict
//                                  
//  /// Extends NameValueCollection with methods to transform it to an enumerable, map or dictionary.
//  type System.Collections.Specialized.NameValueCollection with
//    member this.AsEnumerable() = this |> AsEnumerable
//    member this.ToDictionary() = this |> ToDictionary
//    member this.ToMap() =
//      let folder (h:Map<_,_>) (key:string) =
//        Map.add key this.[key] h 
//      this.AllKeys |> Array.fold (folder) Map.empty
//
//[<AutoOpen>]
//module Parser =
//  open System.Text
//
//  let ascii (s:string) = Encoding.ASCII.GetBytes(s)
//  let utf8  (s:string) = Encoding.UTF8.GetBytes(s)
//
//  let private matchToken pattern =
//    function chr when pattern |> List.exists ((=) chr) -> Some(chr) | _ -> None 
//
//  let private matchTokens pattern exclude input =
//    let chars = Some input
//    let excludeIf chr =
//      // If there are no exclusions, just return the char.
//      if exclude = [] then chars
//      // Otherwise, reverse the selection.
//      else match matchToken exclude chr with
//           | None -> chars
//           | _ -> None
//    let folder st chr =
//      match st with
//      | None -> None
//      | _ ->
//          match matchToken pattern chr with
//          | None -> None
//          // If a match, ensure it is not one of the excluded tokens.
//          | Some(chr') -> excludeIf chr'
//    List.fold folder chars input
//
//  let rec (|Star|_|) f acc s =
//    match f s with
//    | Some (res, rest) -> (|Star|_|) f (res :: acc) rest
//    | None -> Some(acc |> List.rev, s)
//
//  /// Character
//  let (|CHAR|_|) = matchToken [0uy..127uy]
//  /// Upper-case alpha
//  let (|UPALPHA|_|) = matchToken [65uy..90uy]
//  /// Lower-case alpha
//  let (|LOALPHA|_|) = matchToken [97uy..122uy]
//  /// Alpha
//  let (|ALPHA|_|) = function UPALPHA res | LOALPHA res -> Some(res) | _ -> None
//  /// Numeric
//  let (|DIGIT|_|) = matchToken [48uy..57uy]
//  /// Alphanumeric
//  let (|ALPHANUM|_|) = function ALPHA res | DIGIT res -> Some(res) | _ -> None
//  /// Control character
//  let (|CTL|_|) = matchToken ([0uy..31uy] @ [127uy])
//  /// Carriage return
//  let (|CR|_|) = matchToken [13uy]
//  /// Line feed
//  let (|LF|_|) = matchToken [10uy]
//  /// Space
//  let (|SP|_|) = matchToken [32uy]
//  /// Horizontal tab
//  let (|HT|_|) = matchToken [9uy]
//  /// Double quote
//  let (|DQ|_|) = matchToken [34uy]
//  /// Newline (carriage return + line feed)
//  let rec (|CRLF|_|) (input: byte list) =
//    match input with (CR _)::(LF _)::[] -> Some(input) | _ -> None
//  /// Linear Whitespace (LWS)
//  let rec (|LWS|_|) (input: byte list) =
//    let sp = Some 32uy
//    // Must be at least one character.
//    if input.Length < 1 then None
//    else match input with
//         // Check for an optional initial CRLF and ensure it is not the empty list.
//         | (CR _)::(LF _)::tl when tl <> [] -> (|LWS|_|) tl
//         // The rest of the items should be either a space or horizontal tab.
//         | _ -> List.fold (fun st b ->
//                  match st with
//                  | None -> None
//                  | _ -> match b with SP res | HT res -> sp | _ -> None) sp input
//  /// Any byte that is not a CTL (but including LWS chars).
//  let (|TEXT|_|) (input: byte list) =
//    let text = Some input
//    // If no character byte is a CTL return None; otherwise return the original text.
//    List.fold (fun st b -> match b with CTL _ -> None | _ -> text) text input