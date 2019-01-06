using System;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TranslationApi
{
    public class GoogleTranslate
    {
        public static string Translate(string word)
        {
            var toLanguage = "de";//English
            var fromLanguage = "en";//Deutsch
            var z = Uri.EscapeUriString(word);
            var h = WebUtility.HtmlEncode(word);
            var u = WebUtility.UrlEncode(word);
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={fromLanguage}&tl={toLanguage}&dt=t&q={u}";
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
