using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using BitFaster.Caching.Lru;
using Iterum.Logs;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace NCV.Network.Http
{
    public class HttpNetworkClient
    {
        public event Action StatusError;

        private readonly IRequestPreprocessor[] preprocessors;
        private readonly TimeSpan timeout;
        private readonly IProgress<float> progress;

        private ConcurrentTLru<int, ResponseContext> responsesCache;

        private Dictionary<int, Awaitable<ResponseContext>> requestsInProgress;

        private bool logEnabled;

        public string BasePath { get; }

        public HttpNetworkClient(string basePath, TimeSpan timeout, TimeSpan defaultCacheLifeTime, bool logEnabled,
                                 params IRequestPreprocessor[] preprocessors)
            : this(basePath, timeout, defaultCacheLifeTime, null, logEnabled, preprocessors)
        {
        }

        public HttpNetworkClient(string basePath, TimeSpan timeout, TimeSpan defaultCacheLifeTime,
                                 IProgress<float> progress, bool logEnabled,
                                 params IRequestPreprocessor[] preprocessors)
        {
            this.BasePath   = basePath;
            this.timeout    = timeout;
            this.progress   = progress;
            this.logEnabled = logEnabled;

            this.preprocessors = new IRequestPreprocessor[preprocessors.Length];
            Array.Copy(preprocessors, this.preprocessors, preprocessors.Length);

            responsesCache = new ConcurrentTLru<int, ResponseContext>(100, defaultCacheLifeTime);

            requestsInProgress = new Dictionary<int, Awaitable<ResponseContext>>();
        }


        public async Awaitable<T> GetAsync<T>(string path, CancellationToken token) where T : class
        {
            return await GetAsync<T>(path, null, true, token);
        }

        public async Awaitable<T> GetAsyncWithoutCache<T>(string path, CancellationToken token)
            where T : class
        {
            return await GetAsync<T>(path, null, false, token);
        }

        private async Awaitable<T> GetAsync<T>(string path, object value, bool useCache,
                                               CancellationToken cancellationToken,
                                               ContentType contentType = ContentType.Json) where T : class
        {
            var request = new RequestContext(HttpMethod.GET, BasePath, path, value);

            int hash = request.GetHash();

            if (!responsesCache.TryGet(hash, out var response))
            {
                // response = await SendAsync(request, cancellationToken);

                if (useCache)
                {
                    if (requestsInProgress.TryGetValue(hash, out var lazyTask))
                    {
                        if (logEnabled)
                            Debug.LogWarning($"Request in progress: {request.FinalRequestPath}");
                        response = await lazyTask;
                    }
                    else
                    {
                        var task = SendAsync(request, cancellationToken);

                        requestsInProgress.Add(hash, task);

                        response = await task;
                    }
                }
                else
                {
                    response = await SendAsync(request, cancellationToken);
                }

                requestsInProgress.Remove(hash);

                if (!response.IsCanceled && useCache)
                    responsesCache.AddOrUpdate(hash, response);
            }

            // redirect
            if (response.StatusCode is 401 or 302)
            {
                InvokeStatusError(response);
            }

            return response.TryGetResponseContextOrError<T>(this);
        }

        public async Awaitable<T> PostAsync<T>(string path, string value,
                                               CancellationToken cancellationToken) where T : class
        {
            return (await HttpAsync(path, HttpMethod.POST, value, false, cancellationToken))
                .TryGetResponseContextOrError<T>(this);
        }

        public async Awaitable PostAsync(string path, string value,
                                         CancellationToken cancellationToken)
        {
            await HttpAsync(path, HttpMethod.POST, value, false, cancellationToken);
        }

        public async Awaitable DeleteAsync(string path, CancellationToken cancellationToken)
        {
            await HttpAsync(path, HttpMethod.DELETE, string.Empty, false, cancellationToken);
        }

        public async Awaitable PutAsync(string path, string value,
                                        CancellationToken cancellationToken)
        {
            await HttpAsync(path, HttpMethod.PUT, value, false, cancellationToken);
        }

        public async Awaitable<T> PutAsync<T>(string path, string value,
                                              CancellationToken cancellationToken) where T : class
        {
            return (await HttpAsync(path, HttpMethod.PUT, value, false, cancellationToken))
                .TryGetResponseContextOrError<T>(this);
        }

        public async Awaitable UploadAsync(byte[] bytes, string name, string path,
                                           CancellationToken cancellationToken)
        {
#if UNITY_EDITOR
            var formData = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", bytes, name, "application/pdf")
            };
            await HttpAsync(path, HttpMethod.POST, formData, false, cancellationToken, ContentType.Stream);
#else
            var form = new WWWForm();
            form.AddBinaryData("file", bytes, name, "application/pdf");
            await HttpAsync(path, HttpMethod.POST, form, false, cancellationToken, ContentType.Stream);
#endif
        }

        public async Awaitable<ResponseContext> HttpAsync(string path, HttpMethod method, object value, bool useCache,
                                                          CancellationToken cancellationToken,
                                                          ContentType contentType = ContentType.Json)
        {

            var request = new RequestContext(method, BasePath, path, value);
            request.ContentType = contentType;

            int hash = request.GetHash();


            if (!responsesCache.TryGet(hash, out var response))
            {
                if (useCache)
                {
                    if (requestsInProgress.TryGetValue(hash, out var lazyTask))
                    {
                        if (logEnabled)
                            Log.Warn("HttpNetworkClient", $"Request in progress: {request.FinalRequestPath}");
                        response = await lazyTask;
                    }
                    else
                    {
                        var task = SendAsync(request, cancellationToken);
                        requestsInProgress.Add(hash, task);

                        response = await task;
                    }
                }
                else
                {
                    response = await SendAsync(request, cancellationToken);
                }

                requestsInProgress.Remove(hash);

                if (!response.IsCanceled && useCache)
                    responsesCache.AddOrUpdate(hash, response);
            }


            // redirect
            if (response.StatusCode is 401 or 302)
            {
                InvokeStatusError(response);
            }

            return response;
        }

        public async Awaitable<ResponseContext> SendAsync(RequestContext context, CancellationToken cancellationToken)
        {
            if (cancellationToken == default)
            {
                throw new Exception(
                    $"!!!!!! SendAsync '{context.HttpMethod.ToString()}' '{context.FinalRequestPath}' cancellationToken can't be default.");
            }

            bool canceled = false;
            if (logEnabled)
            {
                var log = $"Start {context.HttpMethod}: {context.FinalRequestPath}";
                if (context.HttpMethod != HttpMethod.GET && context.Value is string)
                    log += "... \n" + context.Value;
                Log.Debug(log);
            }

            for (int i = 0; i < preprocessors.Length; i++)
                preprocessors[i].Preprocess(context);

            // send JSON in body as parameter
            UnityWebRequest req;

            if (context.HttpMethod == HttpMethod.GET)
            {
                req = UnityWebRequest.Get(context.FinalRequestPath);
#if !UNITY_EDITOR
                req.redirectLimit = 0;
#endif
            }
            else
            {
                if (context.ContentType == ContentType.Json)
                {
                    var bodyStr = (string)context.Value;

                    req = UnityWebRequest.Put(context.FinalRequestPath, bodyStr);

                    req.SetRequestHeader("accept", "*/*");
                    req.SetRequestHeader("content-type", "application/json; charset=UTF-8");

                    req.method = context.HttpMethod.ToString();
                }

                else if (context.ContentType == ContentType.Stream)
                {
#if UNITY_EDITOR
                    var bodyStr = (List<IMultipartFormSection>)context.Value;
                    req = UnityWebRequest.Post(context.FinalRequestPath, bodyStr);

#else
                    Debug.Log($"Send bodyWWW");
                    var bodyWWW = (WWWForm)context.Value;
                    req = UnityWebRequest.Post(context.FinalRequestPath, bodyWWW);
#endif
                }
                else
                {
                    req = UnityWebRequest.Post(context.FinalRequestPath, (Dictionary<string, string>)context.Value);
                }

                req.redirectLimit = 3;
            }

            var header = context.GetRawHeaders();
            if (header != null)
            {
                foreach (var item in header)
                    req.SetRequestHeader(item.Key, item.Value);
            }

            // var linkToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // linkToken.CancelAfterSlim(timeout);

            // todo: refactor this
#if UNITY_EDITOR
            req.SetRequestHeader("Authorization", "Basic dGVzdGluZzpnc2ElJiRkZmlVRlJpU0lEN2drZmdhXks=");
#endif

            var sw = Stopwatch.StartNew();
            try
            {
                await req.SendWebRequest();

                sw.Stop();
            }
            catch (OperationCanceledException)
            {
                if (logEnabled)
                    Log.Debug($"Canceled {context.HttpMethod}: {context.FinalRequestPath}");
                canceled = true;
            }
            catch (Exception ex)
            {
                if (logEnabled)
                {
                    Log.Error("Request", $"Error {context.HttpMethod}: {ex.Message}");
                }
            }
            finally
            {
                // stop CancelAfterSlim's loop
                // if (!linkToken.IsCancellationRequested) linkToken.Cancel();
            }

            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
            }

            var responseContext =
                new ResponseContext(req.downloadHandler.data, req.responseCode, req.GetResponseHeaders());
            responseContext.IsCanceled = canceled;

            req.Dispose();

            if (logEnabled && !canceled)
            {
                var elapsed        = sw.ElapsedMilliseconds;
                var responseAsText = responseContext.GetResponseAsText();

                var asText = responseAsText.Length < 10000 ? responseAsText : string.Empty;

                switch (elapsed)
                {
                    case > 300 and <= 1000:
                        Debug.Log(
                            $"<color=#f90>Complete {context.HttpMethod}: {context.FinalRequestPath} Elapsed: {sw.Elapsed.ToString()}</color> ..\n" +
                            asText);
                        break;
                    case > 1000:
                        Log.Debug(
                            $"<color=#f55>Complete {context.HttpMethod}: {context.FinalRequestPath} Elapsed: {sw.Elapsed.ToString()}</color> ...\n" +
                            asText);
                        break;
                    default:
                        Log.Debug(
                            $"Complete {context.HttpMethod}: {context.FinalRequestPath} Elapsed: {sw.Elapsed.ToString()} ... \n" +
                            asText);
                        break;
                }
            }

            if (canceled)
            {
                var msg = $"Canceled {context.HttpMethod}: {context.FinalRequestPath}";
                Log.Error(nameof(HttpNetworkClient), msg);
                throw new OperationCanceledException(msg);
            }

            return responseContext;
        }


        public void InvokeStatusError(ResponseContext response)
        {
            if (response.IsCanceled)
            {
                var msg = $"Canceled response on get data {response.StatusCode}";
                Log.Error(nameof(HttpNetworkClient), msg);
                throw new OperationCanceledException(msg);
            }

            StatusError?.Invoke();
        }
    }

    public static class Utils
    {
        public static T TryGetResponseContextOrError<T>(this ResponseContext response, HttpNetworkClient client)
            where T : class
        {

            if (!response.TryGetResponseAs<T>(out var responseObject))
            {
                client.InvokeStatusError(response);
            }

            return responseObject;
        }
    }
}
