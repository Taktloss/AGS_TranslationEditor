using System;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TranslationApi
{
    public class GoogleTranslate : ITranslateAPI
    {
        public string Translate(string text, string from, string to)
        {
            var z = Uri.EscapeUriString(text);
            var h = WebUtility.HtmlEncode(text);
            var u = WebUtility.UrlEncode(text);
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={from}&tl={to}&dt=t&q={u}";
            var webClient = new WebClient
            {
                Encoding = System.Text.Encoding.UTF8
            };
            var result = webClient.DownloadString(url);
            try
            {
                JArray array = JArray.Parse(result);
                var translationParts = array[0].Select( item => new { Translation = item[0].Value<string>() });
                result = string.Join(" ", translationParts.Select( item => item.Translation));

                return result;
            }
            catch (WebException ex)
            {
                return ex.Message;
            }


        }
    }
}
