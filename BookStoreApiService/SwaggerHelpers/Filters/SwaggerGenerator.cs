﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Web.Http.Description;
using Newtonsoft.Json;
using Swashbuckle.Swagger;

namespace BookStoreApiService.SwaggerHelpers.Filters
{
    public class SwaggerGeneratorExt : ISwaggerProvider
    {
        private readonly IApiExplorer _apiExplorer;
        private readonly JsonSerializerSettings _jsonSerializerSettings;
        private readonly IDictionary<string, Info> _apiVersions;
        private readonly SwaggerGeneratorOptions _options;

        public SwaggerGeneratorExt(ISwaggerProvider sp)
        {
            var sg = (SwaggerGenerator)sp;
            var privateFields = sg.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            _apiExplorer = privateFields.First(pf => pf.Name == "_apiExplorer").GetValue(sg) as IApiExplorer;
            _jsonSerializerSettings = privateFields.First(pf => pf.Name == "_jsonSerializerSettings").GetValue(sg) as JsonSerializerSettings;
            _apiVersions = privateFields.First(pf => pf.Name == "_apiVersions").GetValue(sg) as IDictionary<string, Info>;
            _options = privateFields.First(pf => pf.Name == "_options").GetValue(sg) as SwaggerGeneratorOptions;
        }

        public SwaggerDocument GetSwagger(string rootUrl, string apiVersion)
        {
            var schemaRegistry = new SchemaRegistry(
                _jsonSerializerSettings,
                _options.CustomSchemaMappings,
                _options.SchemaFilters,
                _options.ModelFilters,
                _options.IgnoreObsoleteProperties,
                _options.SchemaIdSelector,
                _options.DescribeAllEnumsAsStrings,
                _options.DescribeStringEnumsInCamelCase,
                _options.ApplyFiltersToAllSchemas);

            Info info;
            _apiVersions.TryGetValue(apiVersion, out info);
            if (info == null)
                throw new UnknownApiVersion(apiVersion);

            var paths = GetApiDescriptionsFor(apiVersion)
                .Where(apiDesc => !(_options.IgnoreObsoleteActions && apiDesc.IsObsolete()))
                .OrderBy(_options.GroupingKeySelector, _options.GroupingKeyComparer)
                //.GroupBy(apiDesc => apiDesc.RelativePathSansQueryString())
                .GroupBy(apiDesc => apiDesc.RelativePath)
                .ToDictionary(group => "/" + group.Key, group => CreatePathItem(group, schemaRegistry));

            var rootUri = new Uri(rootUrl);
            var port = (!rootUri.IsDefaultPort) ? ":" + rootUri.Port : string.Empty;

            var swaggerDoc = new SwaggerDocument
            {
                info = info,
                host = rootUri.Host + port,
                basePath = (rootUri.AbsolutePath != "/") ? rootUri.AbsolutePath : null,
                schemes = (_options.Schemes != null) ? _options.Schemes.ToList() : new[] { rootUri.Scheme }.ToList(),
                paths = paths,
                definitions = schemaRegistry.Definitions,
                securityDefinitions = _options.SecurityDefinitions
            };

            foreach (var filter in _options.DocumentFilters)
            {
                filter.Apply(swaggerDoc, schemaRegistry, _apiExplorer);
            }

            return swaggerDoc;
        }

        private IEnumerable<ApiDescription> GetApiDescriptionsFor(string apiVersion)
        {
            return (_options.VersionSupportResolver == null)
                ? _apiExplorer.ApiDescriptions
                : _apiExplorer.ApiDescriptions.Where(apiDesc => _options.VersionSupportResolver(apiDesc, apiVersion));
        }

        private PathItem CreatePathItem(IEnumerable<ApiDescription> apiDescriptions, SchemaRegistry schemaRegistry)
        {
            var pathItem = new PathItem();

            // Group further by http method
            var perMethodGrouping = apiDescriptions
                .GroupBy(apiDesc => apiDesc.HttpMethod.Method.ToLower());

            foreach (var group in perMethodGrouping)
            {
                var httpMethod = group.Key;

                var apiDescription = (group.Count() == 1)
                    ? group.First()
                    : _options.ConflictingActionsResolver(group);

                switch (httpMethod)
                {
                    case "get":
                        pathItem.get = CreateOperation(apiDescription, schemaRegistry);
                        break;
                    case "put":
                        pathItem.put = CreateOperation(apiDescription, schemaRegistry);
                        break;
                    case "post":
                        pathItem.post = CreateOperation(apiDescription, schemaRegistry);
                        break;
                    case "delete":
                        pathItem.delete = CreateOperation(apiDescription, schemaRegistry);
                        break;
                    case "options":
                        pathItem.options = CreateOperation(apiDescription, schemaRegistry);
                        break;
                    case "head":
                        pathItem.head = CreateOperation(apiDescription, schemaRegistry);
                        break;
                    case "patch":
                        pathItem.patch = CreateOperation(apiDescription, schemaRegistry);
                        break;
                }
            }

            return pathItem;
        }

        private Operation CreateOperation(ApiDescription apiDesc, SchemaRegistry schemaRegistry)
        {
            var parameters = apiDesc.ParameterDescriptions
                .Select(paramDesc =>
                {
                    string location = GetParameterLocation(apiDesc, paramDesc);
                    return CreateParameter(location, paramDesc, schemaRegistry);
                })
                 .ToList();

            var responses = new Dictionary<string, Response>();
            var responseType = apiDesc.ResponseType();
            if (responseType == null || responseType == typeof(void))
                responses.Add("204", new Response { description = "No Content" });
            else
                responses.Add("200", new Response { description = "OK", schema = schemaRegistry.GetOrRegister(responseType) });

            var operation = new Operation
            {
                tags = new[] { _options.GroupingKeySelector(apiDesc) },
                operationId = apiDesc.FriendlyId(),
                produces = apiDesc.Produces().ToList(),
                consumes = apiDesc.Consumes().ToList(),
                parameters = parameters.Any() ? parameters : null, // parameters can be null but not empty
                responses = responses,
                deprecated = apiDesc.IsObsolete()
            };

            foreach (var filter in _options.OperationFilters)
            {
                filter.Apply(operation, schemaRegistry, apiDesc);
            }

            return operation;
        }

        private string GetParameterLocation(ApiDescription apiDesc, ApiParameterDescription paramDesc)
        {
            if (apiDesc.RelativePathSansQueryString().Contains("{" + paramDesc.Name + "}"))
                return "path";
            else if (paramDesc.Source == ApiParameterSource.FromBody && apiDesc.HttpMethod != HttpMethod.Get)
                return "body";
            else
                return "query";
        }

        private Parameter CreateParameter(string location, ApiParameterDescription paramDesc, SchemaRegistry schemaRegistry)
        {
            var parameter = new Parameter
            {
                @in = location,
                name = paramDesc.Name
            };

            if (paramDesc.ParameterDescriptor == null)
            {
                parameter.type = "string";
                parameter.required = true;
                return parameter;
            }

            parameter.required = location == "path" || !paramDesc.ParameterDescriptor.IsOptional;
            parameter.@default = paramDesc.ParameterDescriptor.DefaultValue;

            var schema = schemaRegistry.GetOrRegister(paramDesc.ParameterDescriptor.ParameterType);
            if (parameter.@in == "body")
                parameter.schema = schema;
            else
                parameter.PopulateFrom(schema);

            return parameter;
        }
    }
}