using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TranslationApi
{
    public class BingTranslator : ITranslateAPI
    {
        public string Translate(string text, string from, string to)
        {

            string URI = "https://www.bing.com/ttranslate?&category=";
            string myParameters = $"text={text}&from={from}&to={to}";

            using (WebClient client = new WebClient())
            {
                /*NameValueCollection requestParams = new NameValueCollection();
                requestParams.Add("text", "How do you do?");
                requestParams.Add("from", "en");
                requestParams.Add("to", "de?");
                client.UploadValuesAsync(new Uri(URI), requestParams);
                */
                client.Encoding = Encoding.UTF8;
                client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                string response = client.UploadString(URI, myParameters);

                BingResponse bingResponse = JsonConvert.DeserializeObject<BingResponse>(response);
                return bingResponse.translationResponse;
            }
        }
    }

    class BingResponse
    {
        public int statusCode { get; set; }
        public string translationResponse { get; set; }
    }
}
