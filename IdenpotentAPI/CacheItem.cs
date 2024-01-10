using System.Web.Mvc;

namespace IdempotentAPI
{
    /// <summary>
    /// Object type used in the request/result cache.
    /// </summary>
    public class CacheItem
    {
        /// <summary>
        /// Unique id for every request. 
        /// This have to unique per request even if the idempotency key is the same.
        /// </summary>
        public string RequestId { get; private set; }

        /// <summary>
        /// Result of the HTTP operation.
        /// This value is null until the request execution is in progress.
        /// </summary>
        public ActionResult Result { get; private set; }

        /// <summary>
        /// Data hash from the request payload.
        /// It can be used to check if a request payload is identical with another.
        /// </summary>
        public string RequestDataHash { get; private set; }

        /// <summary>
        /// False when the request is still processing and no result stored yet.
        /// </summary>
        public bool IsRequestDone
        {
            get
            {
                return this.Result != null;
            }
        }

        /// <summary>
        /// Create a new cache item for a request.
        /// </summary>
        /// <param name="requestId">Unique request id. This have to unique per request even if the idempotency key is the same</param>
        /// <param name="requestDataHash">Data hash from the request payload</param>
        public CacheItem(string requestId, string requestDataHash)
        {
            this.RequestId = requestId;
            this.RequestDataHash = requestDataHash;
        }

        /// <summary>
        /// Stores the result of the request.
        /// </summary>
        public void SetResult(ActionResult actionResult)
        {
            this.Result = actionResult;
        }
    }
}
