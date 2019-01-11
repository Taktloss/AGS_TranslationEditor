using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;


namespace TranslationApi
{
    public class LingueeTranslator : ITranslateAPI
    {
        public string Translate(string text, string from, string to)
        {
            string url = @"https://www.linguee.com/english-german/search?source=auto&query=How+do+you+do";

            HtmlWeb html = new HtmlWeb();
            html.AutoDetectEncoding = true;
            var htmlDoc = html.Load(url);

            var translationNode = htmlDoc.DocumentNode.SelectNodes("//*[@class='dictLink featured']");

            return translationNode[0].InnerText;
        }


    }
    public static class HtmlNodeExtensions
    {
        public static IEnumerable<HtmlNode> GetElementsByClassName(this HtmlNode parent, string name)
        {
            return parent.Descendants().Where(node => node.HasClass(name));//.Name == name);
        }

        public static IEnumerable<HtmlNode> GetElementsByName(this HtmlNode parent, string name)
        {
            return parent.Descendants().Where(node => node.Name == name);
        }

        public static IEnumerable<HtmlNode> GetElementsByTagName(this HtmlNode parent, string name)
        {
            return parent.Descendants(name);
        }
    }
}
