using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TranslationApi
{
    public interface ITranslateAPI
    {
        string Translate(string text, string from, string to);
    }
}