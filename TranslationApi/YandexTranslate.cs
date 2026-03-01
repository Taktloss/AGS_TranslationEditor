using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace TranslationApi
{
    public class YandexTranslator
    {
        /// <summary>
        /// Translates the text.
        /// </summary>
        /// <param name="apikey">Your Yandex ApiKey</param>
        /// <param name="lang">Translation direction (for example, "en-ru" or "ru")</param>
        /// <param name="text">The text to be translated.</param>
        public static async Task<string> Translate(string apikey, string lang, string text)
        {
            string req = $"https://translate.yandex.net/api/v1.5/tr/translate?key={apikey}&lang={lang}&text={Uri.EscapeUriString(text)}";
            using (HttpClient httpClient = new HttpClient())
            {
                string result = await httpClient.GetStringAsync(req);
                using (XmlReader xreader = XmlReader.Create(new StringReader(result)))
                {
                    xreader.ReadToFollowing("text");
                    return xreader.ReadElementContentAsString();
                }
            }
        }
    }
}