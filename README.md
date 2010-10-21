Frank
============
Frank is a simple, domain specific language for writing web applications on top of [Frack](http://nwsgi.net/). Frank is inspired by the Ruby dynamic duo of Rack and Sinatra and aims to provide the simplicity of those frameworks to .NET web developers.  

Usage
============

Define a Frank application:

    > let myApp = FrankApp [
    >   get "/" (fun _ -> Object(Str("Hello world!")))
    >   post "/order" (fun params -> Object(Obj(createOrder(params))))
    > ]
    > let frackApp = (myApp.Invoke)

Todo / Design decisions
============
1. Choice: Discriminated union or explicit cast?
    * If the former, don't rely on Frack.Value but create a Frank specific type.
    * If the latter, how do you prevent the implementer from having to use `:> obj` everywhere?
2. Should routing filters be stored in a lookup dictionary to find the appropriate handler? (done)
3. Apply a better pattern match to path filters. (done - Regex)
4. Create a params hash from the incoming request.
5. <del>Is model binding a good idea or should that be another layer?</del>
6. Introduce the State monad for retrieving/writing Request/Response throughout app composition.
7. Samples

Team
============
* Ryan Riley (@panesofglass)
* Chris Holt

Thanks
============
* Don Syme for creating F#.
* Rack for demonstrating how simplicity can be enormously powerful.
* Sinatra for inspiration in design and, obviously, the name.
