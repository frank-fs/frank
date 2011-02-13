Frack
============
Frack -- Functional Rack -- is an implementation of [OWIN](http://owin.github.com/owin) (Open Web Interface for .NET), which has a similar intent as the [Python WSGI](http://www.python.org/dev/peps/pep-0333/) and [JSGI](http://jackjs.org/jsgi-spec.html) specifications. Frack is similar in implementation to [Rack](http://rack.rubyforge.org/) and [Jack](http://jackjs.org/) and owes a lot to those projects.

Frack is developed in [F#](http://fsharp.net) as it most directly correlates to the Python and JavaScript implementations and provides yet another opportunity to show off the power and elegance of this terrific language.

Goals
============
1. Provide a simple, to-the-metal framework for quickly building web applications without a lot of hassle. Frack will run on top of [ASP.NET](http://asp.net/) or [System.Net.HttpListener](http://msdn.microsoft.com/en-us/library/system.net.httplistener.aspx).
2. Provide a similar interface to those already available on other platforms for easier interoperability with those platforms/applications.
3. Provide a means of using Frack apps as middlewares for other multi-tier applications, similar to [Rack middleware](http://tekpub.com/production/rack).
4. Allow easy deployment on a range of hosts: IIS via ASP.NET, [Kayak](http://kayakhttp.com), etc.

Usage
============

### Define an app

Takes an OWIN request dictionary and handlers for an OWIN response tuple or exception. 

    >  // Writes "Howdy!" then echoes the request body.
    >  let app = Owin.FromAsync (fun request -> async {
    >    let! body = request |> Request.readToEnd
    >    return ("200 OK", dict [| ("Content-Type", "text/plain") |],
    >            seq { yield "Howdy!"B :> obj
    >                  yield body :> obj }) })
    
    val app : Action<IDictionary<string,obj>,Action<string,IDictionary<string,string>,seq<obj>>,Action<exn>>

### Define a middleware

Takes an OWIN app delegate and returns an OWIN app delegate.

    > let head (app: Action<_,Action<_,IDicitonary<_,_>,_>,Action<exn>>)=
    >   let app = app |> Owin.ToAsync
    >   Owin.FromAsync (fun (req: IDictionary<string, obj>) -> async {
    >     if (req?RequestMethod :?> string) <> "HEAD" then
    >       return! app req
    >     else
    >       req?RequestMethod <- "GET"
    >       let! status, headers, _ = app req
    >       return status, headers, Seq.empty })

    val head : Action<IDictionary<string,obj>,Action<string,IDictionary<string,string>,seq<obj>>,Action<exn>> -> Action<IDictionary<string,obj>,Action<string,IDictionary<string,string>,seq<obj>>,Action<exn>>

### Add middlewares to an app.

    > // f(g(x)) style
    > let myApp = printEnvironment head app

    > // using F# pipeline operator
    > let myApp = app |> head |> printEnvironment

    > // using function composition 
    > let myApp = (head >> printEnvironment) app

Other
============
If this interests you, please also check out [Frank](https://github.com/panesofglass/frank).

Team
============
* Ryan Riley

Thanks
============
* [Don Syme](http://blogs.msdn.com/b/dsyme/) for creating [F#](http://fsharp.net).
* [Rack](http://rack.rubyforge.org) for [Ruby](http://www.ruby-lang.org/).
* [WSGI](http://wsgi.org/wsgi) for [Python](http://python.org/).
* [Jack/JSGI](http://jackjs.org) for JavaScript.
