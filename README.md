Frank
============
Frank is a simple, domain specific language in [F#](http://fsharp.net/) for writing web applications. Frank is inspired by the Ruby dynamic duo of [Rack](http://rack.rubyforge.org/) and [Sinatra](http://www.sinatrarb.com/) and aims to provide the simplicity of those frameworks to .NET web developers. Frank also gives a nod to the [Snap](http://snapframework.com/) framework since it beat Frank to the punch on using the state monad.

As of right now, Frank will run on top of both [Frack](http://github.com/panesofglass/frack) and the [WCF Web APIs](http://wcf.codeplex.com/) using the Microsoft.Http libraries.

Usage
============

Define a Frank application:

    > let myApp = FrankApp.init [
    >   get "/" (putPlainText "Hello world!")
    >   post "/order" (fun () -> frank {
    >     let! p = getParams
    >     createThingFromParams p
    >     redirectTo "/" })
    >   // More to come...
    > ]
    val myApp : Func<HttpRequestMessage,HttpResponseMessage>

TODO / Design decisions
============
1. Choice: Discriminated union or explicit cast? (neither; went with HttpContent from Microsoft.Http)
1. Should routing filters be stored in a lookup dictionary to find the appropriate handler? (done)
1. Apply a better pattern match to path filters. (done - Regex via Active Patterns)
1. Create a params hash from the incoming request. (done)
1. <del>Is model binding a good idea or should that be another layer?</del> No model binding for now.
1. Introduce the State monad for retrieving/writing Request/Response throughout app composition. (done)
1. Add additional helper functions for adding headers, different content types, etc.
1. Allow view engines to render objects in templated formats.
1. Switch to a reactive style for subscribing route handlers and pre-/post-middlewares.
1. Samples

Team
============
* Ryan Riley (@panesofglass)
* Interested?

Thanks
============
* [Don Syme](http://blogs.msdn.com/b/dsyme/) for creating [F#](http://fsharp.net/).
* [Rack](http://rack.rubyforge.org/) for demonstrating how simplicity can be enormously powerful.
* [Sinatra](http://www.sinatrarb.com/) for inspiration in design and, obviously, the name.
* [Snap](http://snapframework.com/) for cluing me into using the State monad instead of the Reader and Writer monads separately.
