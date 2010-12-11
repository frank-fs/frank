Frack
============
Frack -- Functional Rack -- is an implementation of [OWIN](http://owin.github.com/owin) (Open Web Interface for .NET), which has a similar intent as the [Python WSGI](http://www.python.org/dev/peps/pep-0333/) and [JSGI](http://jackjs.org/jsgi-spec.html) specifications. Frack is similar in implementation to [Rack](http://rack.rubyforge.org/) and [Jack](http://jackjs.org/) and owes a lot to those projects.

Frack is developed in [F#](http://fsharp.net) as it most directly correlates to the Python and JavaScript implementations and provides yet another opportunity to show off the power and elegance of this terrific language.

Goals
============
1. Provide a simple, to-the-metal framework for quickly building web applications without a lot of hassle. Frack will run on top of [ASP.NET](http://asp.net/) or [System.Net.HttpListener](http://msdn.microsoft.com/en-us/library/system.net.httplistener.aspx).
2. Provide a similar interface to those already available on other platforms for easier interoperability with those platforms/applications.
3. Provide a means of using Frack apps as middlewares for other multi-tier applications, similar to [Rack middleware](http://tekpub.com/production/rack).
4. Allow easy deployment on a range of servers: IIS via ASP.NET, [Kayak](http://kayakhttp.com), etc.

Usage
============

### Define an app

Takes an environment and returns a triple of status code, headers, and body.

    >  let app = Application(fun request ->
    >    ("200 OK",
    >     (dict [| ("Content-Type", seq { yield "text/plain" }); ("Content-Length", seq { yield "14" }) |]),
    >     "Hello ASP.NET!" )})
    
    val app : Application

### Define a middleware

Takes an app and returns an app.

    > let head (app: Owin.IApplication) =
    >   let asyncInvoke (req: Owin.IRequest) = async {
    >     if req.Method <> "HEAD"
    >       then return! app.AsyncInvoke(req)
    >       else let get = Request.Create("GET", req.Uri, req.Headers, req.Items, req.BeginReadBody, req.EndReadBody)
    >            let! resp = app.AsyncInvoke(get)
    >            return Response(resp.Status, resp.Headers, (fun () -> Seq.empty)) :> Owin.IResponse }
    >   Application asyncInvoke :> Owin.IApplication

    val head : Owin.IApplication -> Owin.IApplication

### Add middlewares to an app.

    > let myApp = printEnvironment head app
    
    val myApp : Owin.IApplication

Other
============
If this interests you, please also check out [Frank](http://bitbucket.org/riles01/frank). It's a work in progress, atm; not much to see. It'll soon move to http://github.com/panesofglass/frank.

Team
============
* Ryan Riley (@panesofglass)
* (Contact me if you are interested.)

Thanks
============
* [Don Syme](http://blogs.msdn.com/b/dsyme/) for creating [F#](http://fsharp.net).
* [Rack](http://rack.rubyforge.org) for [Ruby](http://www.ruby-lang.org/).
* [WSGI](http://wsgi.org/wsgi) for [Python](http://python.org/).
* [Jack/JSGI](http://jackjs.org) for JavaScript.
