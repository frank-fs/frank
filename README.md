Frack
============
Frack is an implementation of the proposed NWSGI (.NET Web Server Gateway Interface), which has a similar intent as the [Python WSGI](http://www.python.org/dev/peps/pep-0333/) and [JSGI](http://jackjs.org/jsgi-spec.html) specifications. Frack is similar in implementation to [Rack](http://rack.rubyforge.org/) and [Jack](http://jackjs.org/) and owes a lot to those projects.

Frack is developed in [F#](http://fsharp.net) as it most directly correlates to the Python and JavaScript implementations and provides yet another opportunity to show off the power and elegance of a terrific language.

Goals
============
1. Provide a simple, to-the-metal framework for quickly building web applications without a lot of hassle. Frack will run on top of [ASP.NET](http://asp.net/) or [System.Net.HttpListener](http://msdn.microsoft.com/en-us/library/system.net.httplistener.aspx).
2. Provide a similar interface to those already available on other platforms for easier interoperability with those platforms/applications.
3. Provide a means of using Frack apps as middlewares for other multi-tier applications, similar to [Rack middleware](http://tekpub.com/production/rack).

Usage
============

> // NOTE: This will be changing as more functionality is added!
> // Wrap the context into an env. An extension is available for [HttpListenerContext](http://msdn.microsoft.com/en-us/library/system.net.httplistenercontext.aspx), as well.
> // errors is a StringBuilder that captures errors from 
> let errors, env = context |> Env.create << HttpContextWrapper.createFromHttpContext
>
> // Define an app
> let process env =
>   ( 200,
>     Map.ofList [("Content-Type","text/plain");("Content-Length","5")],
>     seq { "Howdy" })
>
> // Run the app
> process env

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
