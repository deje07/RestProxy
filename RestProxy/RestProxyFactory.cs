using System;
using System.Net.Http;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace RestProxy
{
    /// <summary>
    /// Simple factory to help creating <see cref="RestProxy{T}"/> instances easily.
    /// </summary>
    public class RestProxyFactory {

        private readonly HttpClient _httpClient;

        public RestProxyFactory([NotNull] HttpClient httpClient) {
            _httpClient = httpClient;
        }

        public RestProxyFactory([NotNull] HttpClient httpClient, JsonSerializer serializer) {
            _httpClient = httpClient;
            Serializer = serializer;
        }

        public JsonSerializer Serializer { get; set; }
        public Action<HttpResponseMessage> ResponsePostProcess { get; set; } = null;

        public T Create<T>() => RestProxy<T>.Create(_httpClient, Serializer, ResponsePostProcess);
    }
}
