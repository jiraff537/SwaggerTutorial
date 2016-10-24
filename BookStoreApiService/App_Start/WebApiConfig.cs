﻿using System.Web.Http;
using BookStoreApiService.Controllers.ActionFilters;
using BookStoreApiService.HttpHandlers;

namespace BookStoreApiService
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Web API configuration and services

            // vendor headers
            config.MessageHandlers.Add(new MandatoryHeadersHandler());

            // Basic Authorization attributes
            //config.Filters.Add(new AuthorizeAttribute());
            //config.Filters.Add(new BasicAuthenticationFilter());

            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute("DefaultApi",
                "api/{controller}/{id}",
                new {id = RouteParameter.Optional}
                );
        }
    }
}