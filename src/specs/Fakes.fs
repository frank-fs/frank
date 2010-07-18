namespace Frack.Specs
module Fakes =
  open System
  open System.Collections.Specialized
  open System.IO
  open System.Text
  open System.Web

  let url = new Uri("http://wizardsofsmart.net/something/awesome?name=test&why=how")
    
  let mutable queryString = new NameValueCollection()
  queryString.Add("name","test")
  queryString.Add("why","how")
    
  let mutable headers = new NameValueCollection()
  headers.Add("HTTP_TEST", "value")
  headers.Add("REQUEST_METHOD", "GET")
  
  let createContext m =
    { new HttpContextBase() with
        override this.Request =
          { new HttpRequestBase() with
              override this.HttpMethod = m
              override this.Url = url
              override this.QueryString = queryString
              override this.Headers = headers
              override this.ContentType = "text/plain"
              override this.ContentLength = 5 
              override this.InputStream = new MemoryStream(Encoding.UTF8.GetBytes("Howdy")) :> Stream } }