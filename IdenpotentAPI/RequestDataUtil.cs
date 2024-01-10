using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace IdempotentAPI
{
    /// <summary>
    /// It contains utility methods for request data handling.
    /// </summary>
    public static class RequestDataUtil
    {
        /// <summary>
        /// Hash algorithm used for request data hashing.
        /// </summary>
        private readonly static HashAlgorithm hashAlgorithm = new SHA256CryptoServiceProvider();

        /// <summary>
        /// It generates a hash from HTTP request payload.
        /// </summary>
        /// <param name="httpRequest">HTTP request</param>
        /// <returns>String representation of the hashed request payload</returns>
        public static string GetRequestsDataHash(HttpRequestBase httpRequest)
        {
            List<object> requestsData = new List<object>();

            if (httpRequest.ContentLength > 0
                && httpRequest.InputStream != null
                && httpRequest.InputStream.CanRead
                && httpRequest.InputStream.CanSeek
                )
            {
                using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream())
                {
                    httpRequest.InputStream.Position = 0;
                    httpRequest.InputStream.CopyTo(memoryStream);
                    requestsData.Add(memoryStream.ToArray());
                }
            }

            if (HasFormContentType(httpRequest)
                && httpRequest.Form != null)
            {
                requestsData.Add(httpRequest.Form);
            }

            if (httpRequest.Path != null)
            {
                requestsData.Add(httpRequest.Path.ToString());
            }

            return GetHash(hashAlgorithm, JsonConvert.SerializeObject(requestsData));
        }

        /// <summary>
        /// Hashes the request payload representation.
        /// </summary>
        private static string GetHash(HashAlgorithm hashAlgorithm, string input)
        {
            byte[] data = hashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(input));

            var sBuilder = new StringBuilder();

            foreach (byte dataByte in data)
            {
                sBuilder.Append(dataByte.ToString("x2"));
            }

            return sBuilder.ToString();
        }

        /// <summary>
        /// Check if the request has form content type.
        /// </summary>
        private static bool HasFormContentType(HttpRequestBase httpRequest)
        {
            return httpRequest.ContentType != null 
                && (httpRequest.ContentType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) 
                    || httpRequest.ContentType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase));
        }
    }
}
