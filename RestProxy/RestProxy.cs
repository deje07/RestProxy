using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Newtonsoft.Json;
using NLog;

namespace RestProxy {
    
    /// <summary>
    /// <p>
    /// Transparent proxy for making REST API calls easier, using <see cref="HttpClient"/>.
    /// </p>
    /// <p>
    /// To use it, create a interface, annotating it and its methods with the appropriate attributes,
    /// then request a proxy for it.
    /// </p> 
    /// </summary>
    /// <typeparam name="T">The type of the interface to implement</typeparam>
    public class RestProxy<T> : RealProxy {

        // ReSharper disable once StaticMemberInGenericType
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        [NotNull] private readonly HttpClient _httpClient;
        [NotNull] private readonly JsonSerializer _jsonSerializer;
        [CanBeNull] private readonly string _ns;
        [NotNull] private readonly Action<HttpResponseMessage> _responsePostProcess;

        private readonly Encoding _encoding = Encoding.UTF8;
        private readonly string _contentEncoding = "application/json";

        /// <summary>
        /// Create a new transparent proxy using the specified <see cref="HttpClient"/> and <see cref="JsonSerializer"/>.
        /// </summary>
        /// <param name="httpClient">The <see cref="HttpClient"/> to use.</param>
        /// <param name="jsonSerializer">Optional json serializer. If omitted, defaults will be used.</param>
        /// <param name="responsePostProcess">Optional error handler for response. Default behavior is HttpResponseMessage.EnsureSuccessStatusCode</param>
        /// <returns>The new transparent proxy implementing the requested interface.</returns>
        [PublicAPI] [NotNull]
        public static T Create([NotNull] HttpClient httpClient, 
            [CanBeNull] JsonSerializer jsonSerializer = null, 
            [CanBeNull] Action<HttpResponseMessage> responsePostProcess = null) {
            return (T) new RestProxy<T>(httpClient, jsonSerializer, responsePostProcess).GetTransparentProxy();
        }
        
        private RestProxy(
            [NotNull] HttpClient httpClient, 
            [CanBeNull] JsonSerializer jsonSerializer,
            [CanBeNull] Action<HttpResponseMessage> responsePostProcess) : base(typeof(T)) {
            var contractAttr = typeof(T).GetCustomAttribute<RestContractAttribute>() 
                                                  ?? throw new ArgumentException("Contract type must be annotated with " + typeof(RestContractAttribute).Name);
            _ns = contractAttr.Namespace;
            _httpClient = httpClient;
            _jsonSerializer = jsonSerializer ?? new JsonSerializer();
            _responsePostProcess = responsePostProcess ?? DefaultResponsePostProcess;
            // todo: verify contract methods if they are ok
            Logger.Debug("Initialized {0}", typeof(T));
        }

        //[DebuggerNonUserCode]
        public override IMessage Invoke(IMessage msg) {            
            var methodCall = (IMethodCallMessage)msg;
            var method = (MethodInfo)methodCall.MethodBase;
            if (Logger.IsTraceEnabled) Logger.Trace("Invoke {0}.{1}()", typeof(T), methodCall.MethodName);

            // obtain RestCall attribute
            var attr = method.GetCustomAttribute<RestCallAttribute>() ?? throw new InvalidOperationException("no RestCallAttribute on method " + method.Name);

            var request = new HttpRequestMessage {
                Method = GetHttpMethod(attr.Method)
            };
            if (attr.LongRunning) {
                request.SetTimeout(Timeout.InfiniteTimeSpan);
            }
            
            // Get return type and additional namespace
            var returnType = method.ReturnType;
            var path = attr.Path;

            var queryParams = new Dictionary<string, object>();
            var bodyParams = new Dictionary<string, object>();
            var cancellationToken = CancellationToken.None;
            
            #region gather parameters
            
            // go through method parameters
            var methodParameters = method.GetParameters();
            for (var i = 0; i < methodParameters.Length; i++) {
                var pi = methodParameters[i];
                if (pi.IsOut) throw new NotImplementedException("out parameters are not supported");
                var arg = methodCall.Args[i];    // note: default parameters are supported with this

                // check if parameter is to be included as query parameter
                var queryParamAttr = pi.GetCustomAttribute<QueryParamAttribute>();
                if (queryParamAttr != null) {
                    var paramName = queryParamAttr.Name ?? pi.Name;
                    queryParams[paramName] = arg;
                    continue;
                }
                
                // check if parameter is to be substituted into the url
                var urlParamAttr = pi.GetCustomAttribute<UrlParamAttribute>();
                if (urlParamAttr != null && path != null) {
                    var paramName = urlParamAttr.Name ?? pi.Name;
                    path = path.Replace("{" + paramName + "}", ConvertInput(arg));
                    continue;
                }

                var headerAttr = pi.GetCustomAttribute<HeaderAttribute>();
                if (headerAttr != null) {
                    request.Headers.Add(headerAttr.Name, ConvertInput(arg));
                    continue;
                }

                var bodyAttr = pi.GetCustomAttribute<BodyAttribute>();
                if (bodyAttr != null) {
                    var paramName = bodyAttr.Name ?? pi.Name;
                    bodyParams[paramName] = arg;
                    continue;
                }

                if (arg is CancellationToken token) {
                    cancellationToken = token;
                    continue;
                }

                // fall back to query paramerer if not specified otherwise
                queryParams[pi.Name] = arg;
            }
            
            #endregion

            #region prepare request

            var uri = CombineUri(_httpClient.BaseAddress, _ns, path);

            var uriBuilder = uri == null ? new UriBuilder() : new UriBuilder(uri);

            if (queryParams.Any()) {
                uriBuilder.Query = queryParams.Aggregate(new StringBuilder(), (builder, pair) => {
                        if (builder.Length > 0) builder.Append("&");
                        builder.Append(pair.Key).Append("=").Append(ConvertInput(pair.Value));
                        return builder;
                    }).ToString();
            }

            request.RequestUri = uriBuilder.Uri;
            if (bodyParams.Count == 1 && attr.BodyEncoding == EncodingType.None) {
                request.Content = ConvertBodyInput(bodyParams.First().Value);
            } else if (bodyParams.Count > 0) {
                request.Content = ConvertMultipartBody(bodyParams, attr);
            }
            
            foreach (var headerAttribute in method.GetCustomAttributes<AddHeaderAttribute>()) {
                if (headerAttribute.Values.Length == 1) {
                    request.Headers.Add(headerAttribute.Name, headerAttribute.Values[0]);
                } else {
                    request.Headers.Add(headerAttribute.Name, headerAttribute.Values);
                }
            }

            #endregion

            object ret;
            
            #region invoke based on return type

            // Async stuff with result
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>)) {
                returnType = returnType.GetGenericArguments()[0];   // get the function return type from Task<returnType>

                if (returnType == typeof(HttpResponseMessage)) {
                    ret = NoOpInvoker(request, cancellationToken);
                } else if (returnType == typeof(Stream)) {
                    ret = AsyncStreamInvoker(request, cancellationToken);
                } else {
                    ret = AsyncInvoker(request, returnType, cancellationToken);
                }
            }

            // void async return type
            else if (returnType == typeof(Task)) {
                ret = EmptyTaskInvoker(request, cancellationToken);
            }

            // sync stream type
            else if (returnType == typeof(Stream)) {
                ret = SyncStreamInvoker(request, cancellationToken);
            }
            
            // sync normal type
            else {
                ret = SyncResultInvoker(request, returnType, cancellationToken);
            }
            
            #endregion

            return new ReturnMessage(ret, null, 0, null, methodCall);
        }

        #region Execution and response conversion

        //[DebuggerNonUserCode]
        private Task EmptyTaskInvoker(HttpRequestMessage request, CancellationToken cancellationToken) {
            Logger.Trace("Starting async call: {0}", request);
            // start the asynchronous call
            return _httpClient.SendAsync(request, cancellationToken).ContinueWith(task => {
                _responsePostProcess(task.Result);
            }, cancellationToken);
        }

        //[DebuggerNonUserCode]
        private Task<HttpResponseMessage> NoOpInvoker(HttpRequestMessage request, CancellationToken cancellationToken) {
            Logger.Trace("Starting async call: {0}", request);
            return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        //[DebuggerNonUserCode]
        private object AsyncInvoker(HttpRequestMessage request, Type returnType, CancellationToken cancellationToken) {
            Logger.Trace("Starting async call: {0}", request);
            // start the asynchronous call
            var task = _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            // check if stream is requested
            if (returnType == typeof(Stream)) {
                var streamTask = task.ContinueWith(GetStreamResponse, cancellationToken).Unwrap();
                return streamTask;
            }
            
            // otherwise attach a result extractor
            
            const TaskContinuationOptions opts =
                //TaskContinuationOptions.NotOnCanceled | TaskContinuationOptions.NotOnFaulted;
                TaskContinuationOptions.None;

            var taskParam = Expression.Parameter(typeof(Task<HttpResponseMessage>), "task");    // define lambda parameter
            var resultTaskType = typeof(Task<>).MakeGenericType(returnType);                    // create Task<TResult> type
            var continueWith = ReflectionHelper.GetContinueWith2(resultTaskType);               // get a Task<T>.ContinueWith<U> method
            var convertResponse = ConvertResponseMethod.MakeGenericMethod(returnType);          // make a ConvertResponse<TResult> method

            // compose result extractor task
            var resultTask = continueWith.Invoke(task, new object[] {
                Expression.Lambda(
                    Expression.Call(Expression.Constant(this), convertResponse, taskParam),
                    taskParam).Compile(), 
                cancellationToken,
                opts,
                TaskScheduler.Current
            });
            
            // now unwrap Task<Task<TResult>> to Task<TResult>
            return ReflectionHelper.GetUnwrap(returnType).Invoke(null, new[] { resultTask });            
        }

        //[DebuggerNonUserCode]
        private Task<Stream> AsyncStreamInvoker(HttpRequestMessage request, CancellationToken cancellationToken) {
            Logger.Trace("Starting async stream call: {0}", request);
            return _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ContinueWith(GetStreamResponse, cancellationToken).Unwrap();
        }

        //[DebuggerNonUserCode]
        private Stream SyncStreamInvoker(HttpRequestMessage request, CancellationToken cancellationToken) {
            Logger.Trace("Start sync stream call: {0}", request);
            var response = _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStreamAsync()
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        //[DebuggerNonUserCode]
        private object SyncResultInvoker(HttpRequestMessage request, Type returnType, CancellationToken cancellationToken) {
            // sync normal type
            var response = _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var stream = response.Content.ReadAsStreamAsync()
                .ConfigureAwait(false).GetAwaiter().GetResult();
            using (stream) {
                using (var reader = new StreamReader(stream))
                using (var jsonReader = new JsonTextReader(reader)) {
                    return _jsonSerializer.Deserialize(jsonReader, returnType);
                }
            }
        }

        // ReSharper disable once UnusedMember.Global
        // TODO: reflection needs this public for now, but it should be made private
        //[DebuggerNonUserCode]
        public async Task<TResult> ConvertResponse<TResult>(Task<HttpResponseMessage> task) {
            //Logger.Trace("Convert Response...");
            var ret = await task.ConfigureAwait(false);
            _responsePostProcess(ret);
            using (var stream = await ret.Content.ReadAsStreamAsync().ConfigureAwait(false)) {
                using (var reader = new StreamReader(stream))
                using (var jsonReader = new JsonTextReader(reader)) {
                    return (TResult) _jsonSerializer.Deserialize(jsonReader, typeof(TResult));
                }
            }
        }

        //[DebuggerNonUserCode]
        private async Task<Stream> GetStreamResponse(Task<HttpResponseMessage> task) {
            var ret = await task.ConfigureAwait(false);
            _responsePostProcess(ret);
            return await ret.Content.ReadAsStreamAsync().ConfigureAwait(false);
        }

        // check response error codes and throw exceptions as needed
        private void DefaultResponsePostProcess(HttpResponseMessage response) => response.EnsureSuccessStatusCode();

        #endregion

        #region Input conversion methods

        [CanBeNull]
        private string ConvertInput([CanBeNull] object data) {
            if (data == null) return null;
            if (data.GetType().IsPrimitive) {
                return data.ToString();
            }

            if (data is string s) return s;

            using (var writer = new StringWriter()) {
                _jsonSerializer.Serialize(writer, data);
                var result = writer.ToString();
                Logger.Trace("body content is: {0}", result);
                return result;
            }
        }

        [CanBeNull]
        private HttpContent ConvertBodyInput([CanBeNull] object body) {
            if (body == null) return null;
            switch (body) {
                case HttpContent contentBody:
                    return contentBody;
                case Stream streamBody:
                    return new StreamContent(streamBody);
                case byte[] bufferBody:
                    return new ByteArrayContent(bufferBody);
                default:
                    return new StringContent(ConvertInput(body), _encoding, _contentEncoding); 
            }
        }

        [NotNull]
        private HttpContent ConvertMultipartBody(IDictionary<string, object> bodyParams, RestCallAttribute attr) {
            switch (attr.BodyEncoding) {
                case EncodingType.Multipart:
                    var content = new MultipartContent();
                    foreach (var param in bodyParams) content.Add(ConvertBodyInput(param.Value));
                    return content;
                case EncodingType.MultipartForm:
                    var content2 = new MultipartFormDataContent();
                    foreach (var param in bodyParams) {
                        var p = ConvertBodyInput(param.Value);
                        if (p.Headers.ContentDisposition == null) {
                            p.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = param.Key };
                        }
                        content2.Add(ConvertBodyInput(param.Value), param.Key);
                    }
                    return content2;
                case EncodingType.FormUrlEncoding:
                    return new FormUrlEncodedContent(bodyParams.Select(param => new KeyValuePair<string, string>(param.Key, ConvertInput(param.Value))));
                default:
                    throw new ArgumentException("multipart body must specify encoding type");
            }
        }

        #endregion

        #region Utility
        
        // ReSharper disable once StaticMemberInGenericType
        private static readonly MethodInfo ConvertResponseMethod;

        static RestProxy() {
            ConvertResponseMethod = typeof(RestProxy<T>).GetMethod(nameof(ConvertResponse));
        }

        private static HttpMethod GetHttpMethod(HttpVerb verb) {
            switch (verb) {
                case HttpVerb.Get: return HttpMethod.Get;
                case HttpVerb.Put: return HttpMethod.Put;
                case HttpVerb.Post: return HttpMethod.Post;
                case HttpVerb.Delete: return HttpMethod.Delete;
                default:
                    throw new ArgumentOutOfRangeException(nameof(verb), verb, null);
            }
        }

        private static Uri CombineUri(Uri baseUri, params string[] relative) {
            //var uri = baseUri != null ? baseUri : null;
            var builder = baseUri != null ? new UriBuilder(baseUri) : null;
            foreach (var s in relative) {
                if (builder == null) {
                    if (!string.IsNullOrEmpty(s)) builder = new UriBuilder(s);
                } else {
                    if (!string.IsNullOrEmpty(s)) builder.Path = CombinePath(builder.Path, s);
                }
            }
            return builder?.Uri;
        }

        private static string CombinePath(string p1, string p2) {
            if (p2 == null) return p1;
            if (p1 == null) return p2;
            if (p1.EndsWith("/")) {
                return p2.StartsWith("/") ? p1 + p2.Substring(1) : p1 + p2;
            } else {
                return p2.StartsWith("/") ? p1 + p2 : p1 + "/" + p2;
            }
        }
        
        #endregion
    }
}