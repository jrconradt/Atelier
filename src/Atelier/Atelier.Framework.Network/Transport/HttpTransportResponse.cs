using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Network.Transport
{
    [ContractAttribute("HttpTransportResponse", Version = "1.0", Namespace = "Framework.Network.Transport")]
    public class HttpTransportResponse
    {
        public int StatusCode { get; set; }
        public string Content { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; set; } = new();

        public static HttpTransportResponse Success(string content = "")
        {
            return new HttpTransportResponse
            {
                StatusCode = 200,
                Content = content
            };
        }

        public static HttpTransportResponse Failure(int statusCode, string errorMessage)
        {
            return new HttpTransportResponse
            {
                StatusCode = statusCode,
                Content = errorMessage
            };
        }

        public static implicit operator Outcome(HttpTransportResponse response)
        {
            return response.StatusCode >= 200 && response.StatusCode < 300
                ? Outcome.Success()
                : Outcome.Failure();
        }
    }
}
