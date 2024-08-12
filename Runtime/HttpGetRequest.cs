using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using UnityEngine;

namespace NCV.Network.Http
{
    public static class HttpGetRequestHelper
    {
        public static void SetGetRequestPath(this RequestContext ctx)
        {
            var args = SerializeToParameterizedLine(ctx.Value);

            var path = $"{NormalizePath(ctx.BasePath)}/{NormalizePath(ctx.Path)}{args}";

            ctx.SetFinalRequestPath(path);
        }

        public static void SetPostRequestPath(this RequestContext ctx)
        {
            var path = $"{NormalizePath(ctx.BasePath)}/{NormalizePath(ctx.Path)}";
            ctx.SetFinalRequestPath(path);
        }


        private static string NormalizePath(string url)
        {
            url = url.Trim();

            //
            // Fix url format errors
            //
            //
            // replace win-path slash to web-slash
            url = url.Replace("\\", "/");

            // trim end slash
            url = url.TrimEnd('/');

            return url;
        }

        private static string SerializeToParameterizedLine<T>(T obj) where T : class
        {
            if (obj == null) { return string.Empty; }

            var type = obj.GetType();

            var fields = type.GetFields();
            var properties = type.GetProperties();

            var parameters = new StringBuilder();
            Add(fields, parameters, obj);
            Add(properties, parameters, obj);

            if (parameters.Length > 0)
                parameters.Remove(parameters.Length - 1, 1);

            return $"?{parameters}";
        }

        private static void Add<T>(T[] fields, StringBuilder parameters, object obj) where T : MemberInfo
        {
            foreach (var info in fields)
            {
                object objValue = info switch
                {
                    PropertyInfo property => property.GetValue(obj),
                    FieldInfo field => field.GetValue(obj),
                    _ => null
                };

                // Get name
                string name = info.Name;

                // normalization: first letter lower case
                char[] a = name.ToCharArray();
                a[0] = char.ToLower(a[0]);
                name = new string(a);

                if (info.GetCustomAttribute(typeof(JsonPropertyNameAttribute)) is JsonPropertyNameAttribute attr)
                    name = attr.Name;

                switch (objValue)
                {
                    // Add parameters to url
                    case string value:
                        {
                            if (!string.IsNullOrEmpty(value))
                                parameters.Append($"{name}={value}&");
                            break;
                        }
                    // Types
                    case DateTime value:
                        parameters.Append($"{name}={value:yyyy-MM-dd}&");
                        break;
                    // Arrays
                    case ICollection<long> value:
                        {
                            foreach (long item in value) parameters.Append($"{name}={item.ToString()}&");

                            break;
                        }
                    case List<decimal> value:
                        {
                            foreach (decimal item in value) parameters.Append($"{name}={item.ToString(CultureInfo.InvariantCulture)}&");

                            break;
                        }
                    case List<string> value:
                        {
                            foreach (string item in value) parameters.Append($"{name}={item}&");

                            break;
                        }
                    // Numbers
                    case double value:
                        string strValue = value.ToString(CultureInfo.InvariantCulture).Replace(",", ".");
                        parameters.Append($"{name}={strValue}&");
                        break;
                    case long value:
                        parameters.Append($"{name}={value.ToString()}&");
                        break;
                    case int value:
                        parameters.Append($"{name}={value.ToString()}&");
                        break;
                    case decimal value:
                        parameters.Append($"{name}={value.ToString(CultureInfo.InvariantCulture)}&");
                        break;
                    case bool value:
                        parameters.Append($"{name}={value.ToString().ToLowerInvariant()}&");
                        break;
                    // else
                    default:
                        if (objValue == null)
                        {
                            continue;
                        }
                        Debug.LogError($"{name} of type {objValue?.GetType().Name} not supported");
                        break;
                }
            }
        }
    }
}
