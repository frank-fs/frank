using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Frack;
using Frack.Hosting;

namespace HelloAspNet
{
    public class MvcApplication : HttpApplication
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapFrackRoute("{*page}",
                Frack.Middleware.head(
                    Frack.Application.Create(request =>
                        Frack.Response.Create(
                            "200 OK",
                            new Dictionary<string, IEnumerable<string>>
                            {
                                { "Content-Type", new [] { "text/plain" } },
                                { "Content-Length", new [] { "14" } },
                            },
                            "Hello ASP.NET!"))));
        }

        protected void Application_Start()
        {
            RegisterRoutes(RouteTable.Routes);
        }
    }
}