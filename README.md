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

Takes an environment and returns a triple of status code, headers, and body.
    
    > // Using an App delegate instead of a pure function for better C#/VB interop.
    > let app env = App(fun env ->
    >   ( 200, dict [| ("Content-Type","text/plain");("Content-Length","5") |], seq { yield "Howdy" } ))
    
    val app : App

### Define a middleware

Takes an app and returns an app.

    > let head = App(fun env ->
    >   let status, hdrs, body = app env
    >   match env?HTTP_METHOD with
    >   | Str "HEAD" -> ( status, hdrs, Seq.empty )
    >   | _ -> ( status, hdrs, body ))

    val head : App

### Add middlewares to an app.

    > // I want to get back to the previous approach:
    > // let myApp = app >> head >> auth >> logging
    > let myApp = logging auth head app
    
    val myApp : App

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
