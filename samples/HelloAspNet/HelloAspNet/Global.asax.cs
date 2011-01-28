using System;
using System.Collections.Generic;
using System.Text;
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
                        byte[] bytes = new byte[] { };
                        //request.ReadToEnd(bs => bytes = bs, econt);
        				cont("200 OK",
        					new Dictionary<string, string> { { "Content-Type", "text/plain" } },
        					new[] { Encoding.ASCII.GetBytes("Hello ASP.NET!\r\n"), bytes });
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