Frank
============
Frank is a simple, domain specific language for writing web applications on top of [Frack](http://nwsgi.net/). Frank is inspired by the Ruby dynamic duo of Rack and Sinatra and aims to provide the simplicity of those frameworks to .NET web developers.  

Usage
============

Sinatra-like syntax helpers:

> let myApp = FrankApp([
>   get "/" (fun () -> "Hello world!")
>   post "/order" (fun order -> createOrder(order))
> ])
> run myApp env // The env would be provided from Frack.

Team
============
* Ryan Riley (@panesofglass)
* Chris Holt

Thanks
============
* Don Syme for creating F#.
* Rack for demonstrating how simplicity can be enormously powerful.
* Sinatra for inspiration in design and, obviously, the name.
