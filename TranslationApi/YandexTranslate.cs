/*
    Copyright 2015 Bernd Keilmann

    This file is part of the AGS Translation Editor.

    AGS Translation Editor is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    AGS Translation Editor is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with AGS Translation Editor.  If not, see<http://www.gnu.org/licenses/>.

    Diese Datei ist Teil von AGS Translation Editor.

    AGS Translation Editor ist Freie Software: Sie können es unter den Bedingungen
    der GNU General Public License, wie von der Free Software Foundation,
    Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren
    veröffentlichten Version, weiterverbreiten und/oder modifizieren.

    Fubar wird in der Hoffnung, dass es nützlich sein wird, aber
    OHNE JEDE GEWÄHRLEISTUNG, bereitgestellt; sogar ohne die implizite
    Gewährleistung der MARKTFÄHIGKEIT oder EIGNUNG FÜR EINEN BESTIMMTEN ZWECK.
    Siehe die GNU General Public License für weitere Details.

    Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
    Programm erhalten haben.Wenn nicht, siehe <http://www.gnu.org/licenses/>.
*/

using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace TranslationApi
{
    public class YandexTranslate
    {
        /// <summary>
        /// Translates the text.
        /// </summary>
        /// <param name="apikey">Your Yandex ApiKey</param>
        /// <param name="lang">Translation direction (for example, "en-ru" or "ru")</param>
        /// <param name="text">The text to be translated.</param>
        public static async Task<string> Translate(string apikey, string lang, string text)
        {
            string translation;
            string req = $"https://translate.yandex.net/api/v1.5/tr/translate?key={apikey}&lang={lang}&text={Uri.EscapeUriString(text)}";
            WebClient wc = new WebClient()
            {
                Encoding = Encoding.UTF8
            };

            string result = wc.DownloadString(req);
            var stream = await Task.Run(() => wc.OpenReadTaskAsync(req));
            using (XmlTextReader xreader = new XmlTextReader(stream))// new StringReader(result)))
            {
                xreader.ReadToFollowing("text");
                translation = xreader.ReadElementContentAsString();
            }
            return translation;
            
        }
    }

}