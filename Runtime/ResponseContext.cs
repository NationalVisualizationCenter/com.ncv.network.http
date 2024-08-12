using System;
using System.Collections.Generic;
using System.Text;
using NCV.Base;
using UnityEngine;

namespace NCV.Network.Http
{
    public class ResponseContext
    {
        private readonly byte[] bytes;

        public long StatusCode { get; }
        public Dictionary<string, string> ResponseHeaders { get; }
        public bool IsCanceled { get; set; }

        public ResponseContext(byte[] bytes, long statusCode, Dictionary<string, string> responseHeaders)
        {
            this.bytes = bytes;
            StatusCode = statusCode;
            ResponseHeaders = responseHeaders;
        }

        public byte[] GetRawData()
        {
            return bytes;
        }

        public bool TryGetResponseAs<T>(out T obj) where T : class
        {
            obj = default;

            if (IsCanceled)
            {
                obj = null;
                return false;
            }

            if (typeof(T) == typeof(string))
            {
                obj = GetResponseAsText() as T;
                return true;
            }


            if (bytes == null || bytes.Length == 0)
            {
                obj = null;
                return true;
            }

            try
            {
                obj = Json.Deserialize<T>(GetResponseAsText());
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to deserialize response " + e);
                obj = null;
                return false;
            }
        }



        public string GetResponseAsText()
        {
            if (bytes == null) return string.Empty;
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
