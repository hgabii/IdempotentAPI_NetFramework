using System;
using System.Web.Mvc;

namespace IdempotentAPI
{
    /// <summary>
    /// Action filter attribute which can be used to make non-idempotent POST and PATCH HTTP operations to idempotent
    /// by caching the result of every request and return the cached result when the same request received again.
    /// 
    /// This attribute implements the following idempotency HTTP header field standard: https://datatracker.ietf.org/doc/html/draft-idempotency-header-00
    /// 
    /// When an action is annotated with this attribute: 
    ///   - The method needs to have one of the following result type: JsonResult
    ///   - The client have to include a unique idempotency key HTTP request header in every request!
    ///   
    /// NOTE: .NET Framework MemoryCache is used for caching requests. 
    ///       The cache properties can be changed in 
    ///       configuration -> system.runtime.caching -> memoryCache -> namedCaches section with 'IdempotencyCache' name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public class IdempotentAttribute : ActionFilterAttribute
    {
        private readonly uint slidingExpirationMinutes;
        private readonly string headerKeyName;

        /// <summary>
        /// Makes POST and PATCH API method to idempotent.
        /// Supported result types: JsonResult
        /// </summary>
        /// <param name="slidingExpirationMinutes">Time within which a cache entry must be accessed before the cache entry is evicted from the cache</param>
        /// <param name="headerKeyName">Name of the idempotency key in the HTTP request header</param>
        public IdempotentAttribute(uint slidingExpirationMinutes = 30, string headerKeyName = "Idempotency-Key")
        {
            if (string.IsNullOrWhiteSpace(headerKeyName))
            {
                throw new ArgumentException($"Invalid {nameof(headerKeyName)} parameter");
            }

            this.headerKeyName = headerKeyName;
            this.slidingExpirationMinutes = slidingExpirationMinutes;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            IdempotencyHandler.Instance.HandleRequest(context, this.slidingExpirationMinutes, headerKeyName);

            base.OnActionExecuting(context);
        }

        public override void OnActionExecuted(ActionExecutedContext context)
        {
            IdempotencyHandler.Instance.HandleResponse(context);

            base.OnActionExecuted(context);
        }
    }
}
