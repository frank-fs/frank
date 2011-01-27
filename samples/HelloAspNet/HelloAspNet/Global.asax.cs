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
                    (IDictionary<string, object> request, Action<string, IDictionary<string, string>, IEnumerable<object>> onCompleted, Action<Exception> onError) => {
                        try
                        {
                            onCompleted("200 OK",
                                new Dictionary<string, string> { { "Content-Type", "text/plain" } },
                                new [] { System.Text.Encoding.UTF8.GetBytes("Hello ASP.NET!") });
                        }
                        catch (Exception e)
                        {
                            onError(e);
                        }
                    }));
        }

        protected void Application_Start()
        {
            RegisterRoutes(RouteTable.Routes);
        }
    }
}