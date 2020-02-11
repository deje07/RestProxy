using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace RestProxy {

    /// <summary>
    /// Marks REST contract interfaces. Must be present for <see cref="RestProxy{T}"/>
    /// to be able to generate a proxy.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface)]
    [PublicAPI]
    public class RestContractAttribute : Attribute {
        [PublicAPI] [CanBeNull] public string Namespace { get; set; }

        [PublicAPI]
        public RestContractAttribute(string ns = null) {
            Namespace = ns;
        }
    }

    /// <summary>
    /// REST methods remote call.
    /// </summary>
    /// <inheritdoc />
    [AttributeUsage(AttributeTargets.Method)]
    [PublicAPI]
    public class RestCallAttribute : Attribute {

        /// <summary>
        /// The server-relative path of the method
        /// </summary>
        [PublicAPI]
        [CanBeNull]
        public string Path { get; set; }

        /// <summary>
        /// HTTP method to use. Default is GET.
        /// </summary>
        [PublicAPI]
        public HttpVerb Method { get; set; }
        
        /// <summary>
        /// The content enoding to use when the request contains body parameters 
        /// </summary>
        [PublicAPI]
        public EncodingType BodyEncoding { get; set; }

        /// <summary>
        /// Indicates that the request is a long-running one (like upload) so
        /// request timeout should be completely disabled
        /// </summary>
        [PublicAPI]
        public bool LongRunning { get; set; }

        [PublicAPI]
        public RestCallAttribute(string path = null, EncodingType bodyEncoding = EncodingType.None) {
            Path = path;
            Method = HttpVerb.Get;
            BodyEncoding = bodyEncoding;
        }

        [PublicAPI]
        public RestCallAttribute(HttpVerb method, string path = null, EncodingType bodyEncoding = EncodingType.None) {
            Path = path;
            Method = method;
            BodyEncoding = bodyEncoding;
        }
    }

    /// <summary>
    /// GET metod call
    /// </summary>
    public class GetAttribute : RestCallAttribute {
        public GetAttribute(string path = null) : base(HttpVerb.Get, path) { }
    }

    /// <summary>
    /// POST method call
    /// </summary>
    public class PostAttribute : RestCallAttribute {
        public PostAttribute(string path = null) : base(HttpVerb.Post, path) { }
    }
    
    /// <summary>
    /// PUT method call
    /// </summary>
    public class PutAttribute : RestCallAttribute {
        public PutAttribute(string path = null) : base(HttpVerb.Put, path) { }
    }
    
    /// <summary>
    /// DELETE method call
    /// </summary>
    public class DeleteAttribute : RestCallAttribute {
        public DeleteAttribute(string path = null) : base(HttpVerb.Delete, path) { }
    }

    /// <summary>
    /// Parameters annotated with this attribute will be added as URL query parameter.
    /// </summary>
    /// <example>
    /// <code>
    /// [RestCall("/users/get")]
    /// User GetUser([QueryParam("gid")] string GID);
    /// </code>
    /// The resulting URI will be: /users/get?gid=GID
    /// </example>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class QueryParamAttribute : Attribute {
        [PublicAPI] public string Name { get; set; }

        [PublicAPI]
        public QueryParamAttribute(string name = null) {
            Name = name;
        }
    }

    /// <summary>
    /// Parameters annotated with this attribute will be inserted into the REST call
    /// URL as parameters.
    /// </summary>
    /// <example>
    /// <code>
    /// [RestCall("/users/{GID}/get")]
    /// User GetUser([UrlParam] string GID);
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class UrlParamAttribute : Attribute {
        [PublicAPI] public string Name { get; set; }

        [PublicAPI]
        public UrlParamAttribute(string name = null) {
            Name = name;
        }
    }

    /// <summary>
    /// Marks a parameter to send as body. Multiple body parameters can
    /// be set, this case don't forget to set the proper encoding type on the method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class BodyAttribute : Attribute {
        [PublicAPI]
        public string Name { get; set; }
        
        [PublicAPI]
        public BodyAttribute(string name = null) {
            Name = name;
        }
    }

    /// <summary>
    /// Multipart content encoding
    /// </summary>
    public enum EncodingType {
        None,
        FormUrlEncoding,
        Multipart,
        MultipartForm,
    }

    /// <summary>
    /// Marks a parameter to pass as a header value.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class HeaderAttribute : Attribute {
        [PublicAPI] [NotNull]
        public string Name { get; set; }

        public HeaderAttribute([NotNull] string name) {
            Name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    [PublicAPI]
    public class AddHeaderAttribute : Attribute {
        [PublicAPI] [NotNull]
        public string Name { get; set; }
        [PublicAPI] [NotNull]
        public string[] Values { get; set; }

        public AddHeaderAttribute([NotNull] string name, [NotNull] params string[] values) {
            Name = name;
            Values = values ?? throw new ArgumentNullException(nameof(values));
        }
    }
    
    public enum HttpVerb {
        Get,
        Post,
        Put,
        Delete,
    }
}
