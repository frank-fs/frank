namespace Owin.Extensions

[<AutoOpen>]
[<System.Runtime.CompilerServices.Extension>]
module Extensions =

  // Owin.IRequest Extensions
  [<System.Runtime.CompilerServices.Extension>]
  let Path(request:Owin.IRequest) = request.Uri |> (splitUri >> fst)

  [<System.Runtime.CompilerServices.Extension>]
  let QueryString(request:Owin.IRequest) = request.Uri |> (splitUri >> snd)

  [<System.Runtime.CompilerServices.Extension>]
  let AsyncReadBody(request:Owin.IRequest, buffer, offset, count) =
    Async.FromBeginEnd(buffer, offset, count, request.BeginReadBody, request.EndReadBody)

  [<System.Runtime.CompilerServices.Extension>]
  let AsyncReadBodyToEnd(request:Owin.IRequest, count) =
    let asyncReadBody = (fun (buffer, offset, count) ->
      Async.FromBeginEnd(buffer, offset, count, request.BeginReadBody, request.EndReadBody))
    let readBodyToEnd(count) = async {
      let notFinished = ref true
      let initialSize = if count > 1 then count else 32768
      let buffer = Array.zeroCreate<byte> initialSize
      use ms = new System.IO.MemoryStream()
      while !notFinished do
        let! read = asyncReadBody(buffer, 0, buffer.Length)
        if read <= 0 then notFinished := false
        ms.Write(buffer, 0, read)
      return ms.ToArray() }
    readBodyToEnd(count)

  [<System.Runtime.CompilerServices.Extension>]
  let ReadAsString(request:Owin.IRequest) =
    let bytes = AsyncReadBodyToEnd(request, 0) |> Async.RunSynchronously
    System.Text.Encoding.UTF8.GetString(bytes)

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

    /// <summary>Reads the HTTP request body synchronously.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="offset">The offset at which to start reading.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The number of bytes read.</returns>
    member this.ReadBody(buffer, offset, count) = ReadBody(this, buffer, offset, count)

  // Owin.IResponse Extensions
  [<System.Runtime.CompilerServices.Extension>]
  let StatusCode(response:Owin.IResponse) = response.Status |> (fst << splitStatus) 

  [<System.Runtime.CompilerServices.Extension>]
  let StatusDescription(response:Owin.IResponse) = response.Status |> (snd << splitStatus) 

  type Owin.IResponse with
    member this.StatusCode = StatusCode this
    member this.StatusDescription = StatusDescription this


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
                                  
  /// Extends NameValueCollection with methods to transform it to an enumerable, map or dictionary.
  type System.Collections.Specialized.NameValueCollection with
    member this.AsEnumerable() = seq { for key in this.Keys do yield (key, this.[key]) }
    member this.ToDictionary() = dict (this.AsEnumerable())
    member this.ToMap() =
      let folder (h:Map<_,_>) (key:string) =
        Map.add key this.[key] h 
      this.AllKeys |> Array.fold (folder) Map.empty
