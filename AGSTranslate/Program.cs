using System;
using System.Collections.Generic;
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
            if (args.Length <= 0)
            {
                Console.WriteLine("Missing Arguments.");
                Console.WriteLine("Usage: AGSTranslate.exe <Game EXE> <CSV File> <Outputfile>");
            }
            else
            {
                GameInfo info = GameInfo.GetGameInfo(args[0]);
                string csvFilename = args[1];
                string outFilename = args[2];

                Console.WriteLine("{0}: Converting CSV: {1} to {2}",info.GameTitle, csvFilename, outFilename);

                Dictionary<string,string> items = CSVFormat.OpenCSV(csvFilename);
                Translation.CreateTRA_File(info, outFilename, items);                
            }
       }
    }
}
