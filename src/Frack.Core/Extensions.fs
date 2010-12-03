namespace Frack

[<AutoOpen>]
module Extensions =
  /// Splits a relative Uri string into the path and query string.
  let splitUri (uri) =
    if System.String.IsNullOrEmpty(uri)
      then ("/", "")
      else let arr = uri.Split([|'?'|])
           let path = if arr.[0] = "/" then "/" else arr.[0].TrimEnd('/')
           let queryString = if arr.Length > 1 then arr.[1] else ""
           (path, queryString)

  let splitStatus (status) =
    if System.String.IsNullOrEmpty(status)
      then (200, "OK")
      else let arr = status.Split([|' '|])
           let code = int arr.[0]
           let description = if arr.Length > 1 then arr.[1] else "OK"
           (code, description)

  type Owin.IRequest with
    /// Gets the Uri path.
    member this.Path = this.Uri |> (splitUri >> fst)

    /// Gets the query string.
    member this.QueryString = this.Uri |> (splitUri >> snd)

    /// <summary>Reads the HTTP request body asynchronously.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="offset">The offset at which to start reading.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>An <see cref="Async{T}"/> computation returning the number of bytes read.</returns>
    member this.AsyncReadBody(buffer, offset, count) =
      Async.FromBeginEnd(buffer, offset, count, this.BeginReadBody, this.EndReadBody)

    /// <summary>Reads the HTTP request body asynchronously.</summary>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>An <see cref="Async{T}"/> computation returning the bytes read.</returns>
    /// <remarks>If the <paramref name="count"/> is less than 1, the buffer size is set to 32768.</remarks>
    member this.AsyncReadBody(count) =
      let asyncReadBody = (fun (buffer, offset, count) ->
        Async.FromBeginEnd(buffer, offset, count, this.BeginReadBody, this.EndReadBody))
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

    /// <summary>Reads the HTTP request body synchronously.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="offset">The offset at which to start reading.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The number of bytes read.</returns>
    member this.ReadBody(buffer, offset, count) =
      Async.FromBeginEnd(buffer, offset, count, this.BeginReadBody, this.EndReadBody)
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

/// Extensions to the Array module.
module Array =
  /// Slices out a portion of the array from the start index up to the stop index.
  let slice start stop (source:'a[]) =
    let stop' = ref stop
    if !stop' < 0 then stop' := source.Length + !stop'
    let len = !stop' - start
    [| for i in [0..(len-1)] do yield source.[i + start] |]

/// Extensions to dictionaries.
module Dict =
  open System.Collections.Generic
  let toSeq d = d |> Seq.map (fun (KeyValue(k,v)) -> (k,v))
  let toArray (d:IDictionary<_,_>) = d |> toSeq |> Seq.toArray
  let toList (d:IDictionary<_,_>) = d |> toSeq |> Seq.toList
