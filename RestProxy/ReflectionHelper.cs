using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RestProxy {
    
    internal static class ReflectionHelper {
        
        // remark: reflecion on generic classes and methods is a bit combersome in .NET
        // because of reified generic type system
        
        private static readonly MethodInfo ContinueWithMethod;
        private static readonly MethodInfo ContinueWithMethod2;
        private static readonly MethodInfo UnwrapMethod;

        static ReflectionHelper() {
            
            // get Task<T>.ContinueWith(Task<U>, ContinuationOptions)
            ContinueWithMethod = typeof(Task<HttpResponseMessage>)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(mi => mi.Name == nameof(Task.ContinueWith) && mi.ReturnType.IsGenericType)
                .Where(mi => mi.GetParameters().Length == 2)
                .Where(mi => mi.GetParameters()[1].ParameterType == typeof(TaskContinuationOptions))
                .Where(mi => {
                    var parameterType = mi.GetParameters()[0].ParameterType;
                    return parameterType.IsGenericType && parameterType.GenericTypeArguments[0].IsGenericType;
                })
                .Single();
            
            // get Task<T>.ContinueWith(Task<U>, ContinuationOptions, CancellationToken, TaskScheduler)
            ContinueWithMethod2 = typeof(Task<HttpResponseMessage>)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(mi => mi.Name == nameof(Task.ContinueWith) && mi.ReturnType.IsGenericType)
                .Where(mi => mi.GetParameters().Length == 4)
                .Where(mi => mi.GetParameters()[1].ParameterType == typeof(CancellationToken))
                .Where(mi => mi.GetParameters()[2].ParameterType == typeof(TaskContinuationOptions))
                .Where(mi => mi.GetParameters()[3].ParameterType == typeof(TaskScheduler))
                .Where(mi => {
                    var parameterType = mi.GetParameters()[0].ParameterType;
                    return parameterType.IsGenericType && parameterType.GenericTypeArguments[0].IsGenericType;
                })
                .Single();
            
            UnwrapMethod = typeof(TaskExtensions)
                .GetMethods()
                .Single(mi => mi.Name == nameof(TaskExtensions.Unwrap) && mi.ReturnType.IsGenericType);
        }

        /// <summary>
        /// Get a Task&lt;HttpResponseMessage&gt;.ContinueWith(Task&lt;resultType&gt;, ContinuationOptions) method
        /// </summary>
        public static MethodInfo GetContinueWith(Type resultType) => 
            ContinueWithMethod.MakeGenericMethod(resultType);

        /// <summary>
        /// Get a Task&lt;HttpResponseMessage&gt;.ContinueWith(Task&lt;resultType&gt;, CancellationToken, ContinuationOptions, TaskScheduler) method
        /// </summary>
        public static MethodInfo GetContinueWith2(Type resultType) =>
            ContinueWithMethod2.MakeGenericMethod(resultType);

        /// <summary>
        /// Get a Task.Unwrap&lt;resultType&gt; method
        /// </summary>
        public static MethodInfo GetUnwrap(Type resultType) =>
            UnwrapMethod.MakeGenericMethod(resultType);
    }
}