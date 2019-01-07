using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AGSTools;

namespace AGSTranslate
{
    class Program
    {
        static void Main(string[] args)
       {
            if (args.Length >= 3)
            {
                if (File.Exists(args[0]) && File.Exists(args[1]) && 
                    Path.GetExtension(args[0]).Equals(".exe", StringComparison.Ordinal))
                {
                    GameInfo info = GameInfo.GetGameInfo(args[0]);

                    string inFilename = args[1];
                    string outFilename = args[2];

                    Dictionary<string, string> items;
                    string extension = Path.GetExtension(inFilename);
                    switch (extension)
                    {
                        case ".csv":
                        case ".tsv":
                            Console.WriteLine("{0}: Converting: {1} to {2}", info.GameTitle, inFilename, outFilename);
                            items = CSVFormat.OpenCSV(inFilename);
                            Translation.CreateTRA_File(info, outFilename, items);
                            break;

                        default:
                            Console.WriteLine("Filetype not compatible");
                            break;
                    }

                }
            }
            else
            {
                ShowHelp();
                return;
            }
            
       }

        static void ShowHelp()
        {
            Console.WriteLine("Usage: AGSTranslate.exe <Game EXE> <Input File> <Outputfile>");
            Console.WriteLine("Translate CSV or TRS File to a TRA File that can be used in AGS Games.");
        }
    }
}
