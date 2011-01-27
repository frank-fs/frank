using System;
using System.Collections.Generic;
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
        		Middleware.log(Middleware.head((request, cont, econt) =>
        		{
        			try
        			{
        				//var body = Frack.Request.readBody(request);
        				cont("200 OK",
        					new Dictionary<string, string> { { "Content-Type", "text/plain" } },
        					new[] { System.Text.Encoding.ASCII.GetBytes("Hello ASP.NET!") });
        			}
        			catch (Exception e)
        			{
        				econt(e);
        			}
        		})));
        }

        protected void Application_Start()
        {
            RegisterRoutes(RouteTable.Routes);
        }
    }
}