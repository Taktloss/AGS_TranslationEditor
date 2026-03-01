using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace TranslationApi
{
    public class BingTranslator : ITranslateAPI
    {
        public string Translate(string text, string from, string to)
        {
            string uri = "https://www.bing.com/ttranslate?&category=";
            string myParameters = $"text={text}&from={from}&to={to}";

            using (HttpClient client = new HttpClient())
            {
                var content = new StringContent(myParameters, Encoding.UTF8, "application/x-www-form-urlencoded");
                HttpResponseMessage response = client.PostAsync(uri, content).GetAwaiter().GetResult();
                string responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                BingResponse bingResponse = JsonConvert.DeserializeObject<BingResponse>(responseString);
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
