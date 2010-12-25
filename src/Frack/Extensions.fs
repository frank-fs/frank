namespace Frack

[<AutoOpen>]
[<System.Runtime.CompilerServices.Extension>]
module Extensions =
  // Owin.IRequest Extensions    
  [<System.Runtime.CompilerServices.Extension>]
  let Path(request:Owin.IRequest) = request.Uri |> (SplitUri >> fst)

  [<System.Runtime.CompilerServices.Extension>]
  let QueryString(request:Owin.IRequest) = request.Uri |> (SplitUri >> snd)

  [<System.Runtime.CompilerServices.Extension>]
  let AsyncReadBody(request:Owin.IRequest, buffer, offset, count) =
    Async.FromBeginEnd(buffer, offset, count, request.BeginReadBody, request.EndReadBody)

  [<System.Runtime.CompilerServices.Extension>]
  let AsyncReadBodyToEnd(request:Owin.IRequest, count) =
    let asyncReadBody = (fun (buffer, offset, count) ->
      Async.FromBeginEnd(buffer, offset, count, request.BeginReadBody, request.EndReadBody))
    let readBodyToEnd(count) = async {
      let notFinished = ref true
      let initialSize = if count > 1 then count else (2 <<< 16)
      let buffer = Array.zeroCreate<byte> initialSize
      use ms = new System.IO.MemoryStream()
      while !notFinished do
        let! read = asyncReadBody(buffer, 0, buffer.Length)
        if read <= 0 then notFinished := false
        ms.Write(buffer, 0, read)
      return ms.ToArray() }
    readBodyToEnd(count)

  [<System.Runtime.CompilerServices.Extension>]
  let AsyncReadAsString(request:Owin.IRequest) = async {
    let! bytes = AsyncReadBodyToEnd(request, 2 <<< 16)
    return System.Text.Encoding.UTF8.GetString(bytes) }

  [<System.Runtime.CompilerServices.Extension>]
  let ReadAsString(request:Owin.IRequest) = AsyncReadAsString request |> Async.RunSynchronously

  [<System.Runtime.CompilerServices.Extension>]
  let ReadBody(request:Owin.IRequest, buffer, offset, count) =
    Async.FromBeginEnd(buffer, offset, count, request.BeginReadBody, request.EndReadBody)
    |> Async.RunSynchronously

  type Owin.IRequest with
    /// Gets the Uri path.
    member this.Path = Path this

    /// Gets the query string.
    member this.QueryString = QueryString this

    /// <summary>Reads the HTTP request body asynchronously.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="offset">The offset at which to start reading.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>An <see cref="Async{T}"/> computation returning the number of bytes read.</returns>
    member this.AsyncReadBody(buffer, offset, count) = AsyncReadBody(this, buffer, offset, count)

    /// <summary>Reads the HTTP request body asynchronously.</summary>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>An <see cref="Async{T}"/> computation returning the bytes read.</returns>
    /// <remarks>If the <paramref name="count"/> is less than 1, the buffer size is set to 32768.</remarks>
    member this.AsyncReadBody(count) = AsyncReadBodyToEnd(this, count)

    member this.AsyncReadAsString() = AsyncReadAsString this

    member this.ReadAsString() = ReadAsString this

    /// <summary>Reads the HTTP request body synchronously.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="offset">The offset at which to start reading.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The number of bytes read.</returns>
    member this.ReadBody(buffer, offset, count) = ReadBody(this, buffer, offset, count)

  // Owin.IResponse Extensions
  [<System.Runtime.CompilerServices.Extension>]
  let StatusCode(response:Owin.IResponse) = response.Status |> (fst << SplitStatus) 

  [<System.Runtime.CompilerServices.Extension>]
  let StatusDescription(response:Owin.IResponse) = response.Status |> (snd << SplitStatus) 

  let rec writeTo stream item =
    match item with
    // Matches and iterates a sequence recursively to the stream
    | Enum it -> it |> Seq.iter (writeTo stream)
    // Transfers the bytes to the stream
    | Bytes bs -> let st = new SeqStream(bs) in st.TransferTo(stream)
    // Converts a FileInfo into a SeqStream, then transfers the bytes to the stream
    | File fi -> let st = SeqStream.FromFileInfo(fi) in st.TransferTo(stream)
    // Converts a string into a SeqStream, then transfers the bytes to the stream
    | Str str -> let st = SeqStream.FromString(str) in st.TransferTo(stream)
    // Ignore until I better understand ArraySegment
    | Segment seg -> ()

  [<System.Runtime.CompilerServices.Extension>]
  let WriteToStream(response:Owin.IResponse, stream) = response.GetBody() |> Seq.iter (writeTo stream)

  type Owin.IResponse with
    member this.StatusCode = StatusCode this
    member this.StatusDescription = StatusDescription this
    member this.WriteToStream stream = this.GetBody() |> Seq.iter (writeTo stream)

  // Owin.IApplication Extensions
  [<System.Runtime.CompilerServices.Extension>]
  let AsyncInvoke(app:Owin.IApplication, request) =
    Async.FromBeginEnd(request, app.BeginInvoke, app.EndInvoke)

  [<System.Runtime.CompilerServices.Extension>]
  let Invoke(app:Owin.IApplication, request) =
    Async.FromBeginEnd(request, app.BeginInvoke, app.EndInvoke)
    |> Async.RunSynchronously

  type Owin.IApplication with
    /// <summary>Invokes the application asynchronously.</summary>
    /// <param name="request">The HTTP <see cref="Owin.IRequest"/>.</param>
    /// <returns>An <see cref="Async{T}"/> computation returning an <see cref="Owin.IResponse"/>.</returns>
    member this.AsyncInvoke(request) = Async.FromBeginEnd(request, this.BeginInvoke, this.EndInvoke)

    /// <summary>Invokes the application synchronously.</summary>
    /// <param name="request">The HTTP <see cref="Owin.IRequest"/>.</param>
    /// <returns>An <see cref="Owin.IResponse"/>.</returns>
    member this.Invoke(request) = Async.FromBeginEnd(request, this.BeginInvoke, this.EndInvoke)
                                  |> Async.RunSynchronously

  /// Extends NameValueCollection with methods to transform it to an enumerable.
  [<System.Runtime.CompilerServices.Extension>]
  let AsEnumerable(this:System.Collections.Specialized.NameValueCollection) =
    seq { for key in this.Keys do yield (key, this.[key]) }

  /// Extends NameValueCollection with methods to transform it to a dictionary.
  [<System.Runtime.CompilerServices.Extension>]
  let ToDictionary(this:System.Collections.Specialized.NameValueCollection) = this |> AsEnumerable |> dict
                                  
  /// Extends NameValueCollection with methods to transform it to an enumerable, map or dictionary.
  type System.Collections.Specialized.NameValueCollection with
    member this.AsEnumerable() = this |> AsEnumerable
    member this.ToDictionary() = this |> ToDictionary
    member this.ToMap() =
      let folder (h:Map<_,_>) (key:string) =
        Map.add key this.[key] h 
      this.AllKeys |> Array.fold (folder) Map.empty
