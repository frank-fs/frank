Frack
============
Frack is an implementation of the proposed NWSGI (.NET Web Server Gateway Interface), which has a similar intent as the [Python WSGI](http://www.python.org/dev/peps/pep-0333/) and [JSGI](http://jackjs.org/jsgi-spec.html) specifications. Frack is similar in implementation to [Rack](http://rack.rubyforge.org/) and [Jack](http://jackjs.org/) and owes a lot to those projects.

Frack is developed in [F#](http://fsharp.net) as it most directly correlates to the Python and JavaScript implementations and provides yet another opportunity to show off the power and elegance of a terrific language.

Goals
============
1. Provide a simple, to-the-metal framework for quickly building web applications without a lot of hassle. Frack will run on top of [ASP.NET](http://asp.net/) or [System.Net.HttpListener](http://msdn.microsoft.com/en-us/library/system.net.httplistener.aspx).
2. Provide a similar interface to those already available on other platforms for easier interoperability with those platforms/applications.
3. Provide a means of using Frack apps as middlewares for other multi-tier applications, similar to [Rack middleware](http://tekpub.com/production/rack).
4. Allow easy deployment on a range of servers: IIS via ASP.NET, [Kayak](http://kayakhttp.com), etc.

Usage
============

### Define an app

Takes an environment and returns a triple of status code, headers, and body (or at least it will again soon).

    >  let app = Application.fromAsync (fun request -> async {
    >    return Response.create "200 OK"
    >                           (dict [| ("Content-Type", seq { yield "text/plain" })
    >                                    ("Content-Length", seq { yield "14" }) |])
    >                           (fun () -> "Hello ASP.NET!"B :> System.Collections.IEnumerable) })
    
    val app : Owin.IApplication

### Define a middleware

Takes an app and returns an app.

    > let head (app: Owin.IApplication) =
    >   let asyncInvoke (req: Owin.IRequest) = async {
    >     if req.Method <> "HEAD"
    >       then return! app.AsyncInvoke(req)
    >       else let get = Request.create "GET" req.Uri req.Headers req.Items req.BeginReadBody req.EndReadBody
    >            let! resp = app.AsyncInvoke(get)
    >            return Response.create resp.Status resp.Headers (fun () -> Seq.empty :> System.Collections.IEnumerable) }
    >   Application.fromAsync asyncInvoke

    val head : Owin.IApplication

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
