using System;
using System.Linq;
using System.Web;

namespace IdempotentAPI
{
    /// <summary>
    /// It contains utility methods for idempotency key handling.
    /// </summary>
    public static class IdempotencyKeyUtil
    {
        /// <summary>
        /// It reads the idempotency key value from the HTTP request headers and 
        /// creates a cache key based on the method type, request path and the retrived key value.
        /// </summary>
        /// <param name="request">HTTP request</param>
        /// <param name="idempotencyKeyName">Name of the idempotency key request header</param>
        /// <param name="errorDescription">Description about the reason why can not return a key (null value returned)</param>
        /// <returns>Cache key based on the given idempotency key value or null when no valid key value can be retrieved</returns>
        public static string GetKey(HttpRequestBase request, string idempotencyKeyName, out string errorDescription)
        {
            errorDescription = null;

            try
            {
                string idempotencyKey = request.Headers.GetValues(idempotencyKeyName).SingleOrDefault();

                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    errorDescription = $"{idempotencyKeyName} request header has invalid value.";
                    return null;
                }

                return $"{request.HttpMethod} {request.Path} - {idempotencyKey}";
            }
            catch (ArgumentNullException)
            {
                errorDescription = $"No {idempotencyKeyName} request header found in the request. A {idempotencyKeyName} request header has to be defined!";
                return null;
            }
            catch (InvalidOperationException)
            {
                errorDescription = $"Multiple {idempotencyKeyName} request headers found in the request. Only a single {idempotencyKeyName} request header can be defined!";
                return null;
            }
        }
    }
}
