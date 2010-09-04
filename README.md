Frank
============
Frank is a functional reactive request/response engine for .NET applications. Frank is inspired by the Ruby dynamic duo of Rack and Sinatra and aims to provide the simplity of those frameworks but utilizing the powerful functional reactive paradigm for asynchronous processing.

Frank was begun as an experiment in evaluating the power and simpilicty (or powerful simplicity) of functional reactive programming in F# and the use of F# in real-world application development. (Not that we doubt it.)

Goals
============
1. Provide an event-driven request/response pipeline framework that focuses on runtime composition and simplicity.
2. Provide out-of-the-box implementations for quickly developing websites and WPF/Silverlight applications.
3. Provide a means of using Frank apps as middlewares for other multi-tier applications, similar to Rack middleware.

Milestones
============
* frank.core -> Core interfaces for the request/response engine and a bit of implementation for abstracting the "context" under reader and writer monads (Eek! the M-word!)
* frank.web  -> The web framework, to include implementation of basic Rack-like elements on top of the "context" and a defualt implementation similar in appearance to Sinatra, including extensions to allow use in other .NET languages.
* frank.xaml -> The WPF/Silverlight framework. More to come on this one.

Usage
============

### fsharp.web

Hooking up raw event handlers:

> let httpRequestReceived = new Event<HttpContext>()
> let getRequest = httpRequestReceived |> Event.filter (fun ctx -> ctx.Request.HttpMethod = "GET")
> getRequest
> |> Event.filter (fun ctx -> ctx.Request.Uri.ToString() = "/")
> |> Event.subscribe (fun _ -> "Hello world!")
>
> let postRequest = httpRequestReceived |> Event.filter (fun ctx -> ctx.Request.HttpMethod = "POST")
> postRequest
> |> Event.filter (fun ctx -> ctx.Request.Uri.ToString() = "/order")
> |> Event.map (fun ctx -> bindRequestParamsToOrder(ctx))
> |> Event.subscribe (fun order -> createOrder(order))

Sinatra-like syntax helpers:

> let myApp = App([
>   get "/" (fun () -> "Hello world!")
>   post "/order" (fun order -> createOrder(order))
> ])
> start myApp

Team
============
* Ryan Riley (@panesofglass)
* Chris Holt

Thanks
============
* Don Syme for creating F#.
* Rack for demonstrating how simplicity can be enormously powerful.
* Sinatra for inspiration in design and, obviously, the name.
