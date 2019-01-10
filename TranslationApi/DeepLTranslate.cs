using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TranslationApi
{
    public class DeepLTranslate
    {
        public static string Translate()
        {
            string url = @"https://www2.deepl.com/jsonrpc";
            DeepLJsonObject deepLJson = new DeepLJsonObject
            {
                id = 0,
                jsonrpc = "2.0",
                method = "LMT_handle_jobs",
                @params = new Params()
                {
                    jobs = new List<Job>()
                        {
                            new Job() {
                                kind = "default",
                                quality = "fast",
                                raw_en_sentence = "How do you do today ?",
                                raw_en_context_before = new List<object>(),
                                raw_en_context_after = new List<object>()
                            }
                        },
                    lang = new Lang()
                    {
                        user_preferred_langs = new List<string>()
                        {
                            "EN",
                            "DE"
                        },
                        source_lang_user_selected = "auto",
                        target_lang = "DE"
                    },
                    priority = -1
                    
                }

            };
            var httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
            httpWebRequest.ContentType = "application/json; charset=utf-8";
            httpWebRequest.Method = "POST";
            httpWebRequest.Accept = "application/json; charset=utf-8";

            using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
            {
                string json = JsonConvert.SerializeObject(deepLJson);  

                streamWriter.Write(json);
                streamWriter.Flush();
                streamWriter.Close();

                var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    var result = streamReader.ReadToEnd();
                    Console.WriteLine();
                }
            }

            return url;
        }
    }

    public class Job
    {
        public string kind { get; set; }
        public string raw_en_sentence { get; set; }
        public List<object> raw_en_context_before { get; set; }
        public List<object> raw_en_context_after { get; set; }
        public string quality { get; set; }
    }

    public class Lang
    {
        public List<string> user_preferred_langs { get; set; }
        public string source_lang_user_selected { get; set; }
        public string target_lang { get; set; }
    }

    public class Params
    {
        public List<Job> jobs { get; set; }
        public Lang lang { get; set; }
        public int priority { get; set; }
        public long timestamp { get; set; }
    }

    public class DeepLJsonObject
    {
        public string jsonrpc { get; set; }
        public string method { get; set; }
        public Params @params { get; set; }
        public int id { get; set; }
    }
}
