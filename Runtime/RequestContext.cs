using System.Collections.Generic;

namespace NCV.Network.Http
{
    public enum HttpMethod
    {
        GET,
        POST,
        DELETE,
        PUT
    }

    public enum ContentType
    {
        Dict,
        Json,
        Stream
    }

    public class RequestContext
    {
        private Dictionary<string, string> headers;
        public ContentType ContentType;

        public HttpMethod HttpMethod { get; }
        public string BasePath { get; }
        public string Path { get; }
        public object Value { get; }

        public IDictionary<string, string> RequestHeaders => headers ??= new Dictionary<string, string>();


        public string FinalRequestPath { get; private set; }


        public RequestContext(HttpMethod httpMethod, string basePath, string path, object value)
        {
            HttpMethod = httpMethod;
            BasePath = basePath;
            Path = path;
            Value = value;

            if (HttpMethod == HttpMethod.GET) this.SetGetRequestPath();
            else this.SetPostRequestPath();
        }

        public void SetFinalRequestPath(string path)
        {
            FinalRequestPath = path;
        }

        internal Dictionary<string, string> GetRawHeaders()
        {
            return headers;
        }

        public int GetHash()
        {
            var content = $"{HttpMethod}{BasePath}{FinalRequestPath}";
            return content.GetHashCode();
        }
    }
}
