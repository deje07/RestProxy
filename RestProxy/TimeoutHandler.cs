using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace RestProxy
{
    /// <summary>
    /// Custom HTTP handler to implement per-request timeout handling.
    /// Please note that timeout must be disabled on the <see cref="T:System.Net.Http.HttpClient"/>
    /// otherwise that timeout will still kick in.
    /// </summary>
    /// <inheritdoc />
    [PublicAPI]
    public class TimeoutHandler : DelegatingHandler {

        /// <summary>
        /// Default timeout to use, when it is not specified via the request propeties.
        /// See <see cref="HttpRequestExtensions.GetTimeout"/> for more.
        /// </summary>
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(100);

        [PublicAPI]
        public TimeoutHandler() { }

        [PublicAPI]
        public TimeoutHandler(HttpMessageHandler innerHandler) {
            InnerHandler = innerHandler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) {
            using (var cts = GetCancellationTokenSource(request, cancellationToken)) {
                try {
                    return await base.SendAsync(request, cts?.Token ?? cancellationToken).ConfigureAwait(false);
                } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                    throw new TimeoutException();
                }
            }
        }

        private CancellationTokenSource GetCancellationTokenSource(HttpRequestMessage request,
            CancellationToken cancellationToken) {
            var timeout = request.GetTimeout() ?? DefaultTimeout;
            if (timeout == Timeout.InfiniteTimeSpan) {
                // No need to create a CTS if there's no timeout
                return null;
            } else {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);
                return cts;
            }
        }
    }
}
