using LazyCache;
using log4net;
using System;
using System.Net;
using System.Reflection;
using System.Runtime.Caching;
using System.Web.Mvc;

namespace IdempotentAPI
{
    /// <summary>
    /// It caches the request and result for HTTP operations what are marked as idempotent.
    /// </summary>
    public class IdempotencyHandler
    {
        /// <summary>
        /// Logger instance.
        /// </summary>
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType.Name);

        /// <summary>
        /// Idempotency handler singleton instance.
        /// It is initialized at the first use and disposed when the web app stopped.
        /// </summary>
        private static IdempotencyHandler instance = null;

        /// <summary>
        /// Synchronization object for the singleton instance.
        /// </summary>
        private static readonly object instanceLock = new object();

        /// <summary>
        /// Name of the HttpContext Items entry used to store the cache key during the request execution.
        /// </summary>
        private const string HttpItemsKeyForIdempotencyCacheKey = "IdempotencyCacheKey";

        /// <summary>
        /// Request/Result cache which stores CacheItem type objects.
        /// CacheService implementation is thread safe, so no additional synchronization is used here.
        /// </summary>
        private readonly IAppCache cache;

        private IdempotencyHandler()
        {
            MemoryCache idempotencyInMemoryCache = new MemoryCache("IdempotencyCache");
            this.cache = new CachingService(idempotencyInMemoryCache);
            log.Debug($"IdempotecyHandler initialized - " +
                $"CacheMemoryLimit: {idempotencyInMemoryCache.CacheMemoryLimit}, " +
                $"CachePhysicalMemoryLimit: {idempotencyInMemoryCache.PhysicalMemoryLimit}, " +
                $"CachePollingInterval: {idempotencyInMemoryCache.PollingInterval}");
        }

        /// <summary>
        /// Single IdempotencyHandler instance.
        /// </summary>
        public static IdempotencyHandler Instance
        {
            get
            {
                lock (instanceLock)
                {
                    if (instance == null)
                    {
                        instance = new IdempotencyHandler();
                    }

                    return instance;
                }
            }
        }

        /// <summary>
        /// It handles HTTP API method request.
        /// It checks if idempotentcy can be applied on it: 
        ///   - HTTP method is supported (POST or PATCH)
        ///   - idempotency key request header is defined
        /// If a result is cached for an identical request, it responds with the cached result.
        /// </summary>
        /// <param name="context">The action executing context</param>
        /// <param name="slidingExpirationMinutes">Time within which a cache entry must be accessed before the cache entry is evicted from the cache</param>
        /// <param name="idempotencyHeaderKeyName">Name of the idempotency key in the HTTP request header</param>
        public void HandleRequest(ActionExecutingContext context, uint slidingExpirationMinutes, string idempotencyHeaderKeyName)
        {
            string logStamp = nameof(HandleRequest);

            // Idempotency can be applied on POST and PATCH methods only.
            if (!string.Equals(context.HttpContext.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(context.HttpContext.Request.HttpMethod, "PATCH", StringComparison.OrdinalIgnoreCase))
            {
                log.Debug($"{logStamp} - Idempotency is skipped. HTTP request method is not supported - Method: {context.HttpContext.Request.HttpMethod}");
                return;
            }

            // Idempotency key request header is required for actions annotated with Idempotent attribute.
            // Retreieve idempotency key from request headers and check if it is valid.
            string idempotencyKey = IdempotencyKeyUtil.GetKey(context.HttpContext.Request, idempotencyHeaderKeyName, out string errorDescription);

            if (idempotencyKey == null)
            {
                log.Debug($"{logStamp} - Could not get valid idempotency key - Reason: {errorDescription}");
                context.Result = new HttpStatusCodeResult(HttpStatusCode.BadRequest, errorDescription);
                return;
            }

            // Generate request data hash to be able to check that the same idempotency key is not used for another request.
            string requestDataHash = RequestDataUtil.GetRequestsDataHash(context.HttpContext.Request);

            // Create a unique request id to be able to identify if a new item created in the cache or an already cached item is returned.
            string requestId = Guid.NewGuid().ToString();

            // GetOrAdd() method of the cache is thread safe. So no other synchronization is requrired.
            CacheItem cacheItem = cache.GetOrAdd(
                idempotencyKey,
                () =>
                {
                    log.Debug($"Add request to the cache - IdempotencyKey: {idempotencyKey}, RequestId: {requestId}");
                    return new CacheItem(requestId, requestDataHash);
                },
                new CacheItemPolicy()
                {
                    SlidingExpiration = TimeSpan.FromMinutes(slidingExpirationMinutes),
                });

            if (requestId != cacheItem.RequestId)
            {
                // If a request is already cached and that is still in processing, then reply with Conflict.
                if (!cacheItem.IsRequestDone)
                {
                    log.Info($"{logStamp} - Original request is still processing - " +
                        $"IdempotencyKey: {idempotencyKey}, OriginalRequestId: {cacheItem.RequestId}, CurrentRequestId: {requestId}");
                    context.Result = new HttpStatusCodeResult(HttpStatusCode.Conflict, "The original request is still processing.");
                }
                // If the idempotency key was used for a different request, then reply with Unprocessable Entity.
                else if (cacheItem.RequestDataHash != requestDataHash)
                {
                    log.Info($"{logStamp} - The idempotency key can not be reused with a different request payload - " +
                        $"IdempotencyKey: {idempotencyKey}, CurrentRequestId: {requestId}");
                    context.Result = new HttpStatusCodeResult(422, "The idempotency key can not be reused with a different request payload");
                }
                // If a request with JsonResult is already cached and its done, then let's return the same result.
                else if (cacheItem.Result != null && cacheItem.Result is JsonResult jsonResult)
                {
                    log.Info($"{logStamp} - JsonResult is cached for request. Return the cached result - " +
                        $"IdempotencyKey: {idempotencyKey}, OriginalRequestId: {cacheItem.RequestId}, CurrentRequestId: {requestId}");
                    context.Result = new JsonResult()
                    {
                        ContentEncoding = jsonResult.ContentEncoding,
                        ContentType = jsonResult.ContentType,
                        Data = jsonResult.Data,
                        JsonRequestBehavior = jsonResult.JsonRequestBehavior,
                        MaxJsonLength = jsonResult.MaxJsonLength,
                        RecursionLimit = jsonResult.RecursionLimit
                    };
                }
                else
                {
                    log.Warn($"{logStamp} - {cacheItem.Result.GetType()} result type is not supported by the IdempotentAttribute implementation");
                }
            }
            else
            {
                log.Info($"{logStamp} Idempotency applied - IdempotencyKey: {idempotencyKey}, RequestId: {requestId}");
                context.HttpContext.Items[HttpItemsKeyForIdempotencyCacheKey] = idempotencyKey;
            }
        }

        /// <summary>
        /// It handles HTTP API method response.
        /// It checks if idempotentcy was applied on the request.
        /// If there was no error during method operation and the result type is supported, it caches the result.
        /// </summary>
        /// <param name="context">The action executed context</param>
        public void HandleResponse(ActionExecutedContext context)
        {
            string logStamp = nameof(HandleResponse);

            // Get idempotency key used for cache from HttpContext Items collection.
            // This value have to be defines if the request was added to the cache.
            string idempotencyKey = context.HttpContext.Items[HttpItemsKeyForIdempotencyCacheKey] as string;

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                log.Debug($"{logStamp} - No idempotency was applied for the request");
                return;
            }
            
            // Remove idempotency cache key to do not affect the response.
            context.HttpContext.Items.Remove(HttpItemsKeyForIdempotencyCacheKey);

            // Retreive the cached request.
            CacheItem cacheItem = cache.Get<CacheItem>(idempotencyKey);

            if (cacheItem != null)
            {
                // Do not cache result if it was an error during method operation.
                if (context.Exception != null)
                {
                    log.Warn($"{logStamp} - There was an error during request execution. Do not cache the result - " +
                        $"IdempotencyKey: {idempotencyKey}, RequestId: {cacheItem.RequestId}");
                    cache.Remove(idempotencyKey);
                }
                // Check for supported result types here. This can be extended in the future.
                else if (context.Result.GetType() != typeof(JsonResult))
                {
                    log.Info($"{logStamp} - Request result has unsupported type. Do not cache the result - " +
                        $"IdempotencyKey: {idempotencyKey}, RequestId: {cacheItem.RequestId}");
                    cache.Remove(idempotencyKey);
                }
                // Cache the result of the request.
                else
                {
                    log.Info($"{logStamp} - Cache the result - IdempotencyKey: {idempotencyKey}, RequestId: {cacheItem.RequestId}");
                    cacheItem.SetResult(context.Result);
                }
            }
            else
            {
                // Idempotency was applied on the request, but during the request execution, the item was removed from the cache.
                // The reason can be that the cache limit is reached or there is an error in the implementation of this class.
                log.Warn($"{logStamp} No item found in the cache. Check the IdempotencyCache limit configuration - " +
                    $"IdempotencyKey: {idempotencyKey}, " +
                    $"CachedItemsCount: {(this.cache.ObjectCache as MemoryCache).GetCount()}");
            }
        }
    }
}
