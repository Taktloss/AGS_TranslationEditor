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
using System.Collections.Generic;
using System.IO;

namespace AGSTools
{
    /// <summary>
    /// Dictionary-based translation API. Texts are in TRS form (with escape sequences);
    /// use <see cref="TraFile"/> and <see cref="TrsFile"/> to keep the translation options as well.
    /// </summary>
    public static class Translation
    {
        /// <summary>Reads a TRA file. Throws <see cref="AgsFormatException"/> if the file is invalid.</summary>
        public static Dictionary<string, string> ParseTRA_Translation(string filename) =>
            TraFile.Read(filename).Entries;

        /// <summary>Reads a TRS file (UTF-8 or 8-bit, detected from its header).</summary>
        public static Dictionary<string, string> ParseTRS_Translation(string filename) =>
            TrsFile.Read(filename).Entries;

        /// <summary>Compiles translation entries into a TRA file for the given game.</summary>
        public static IReadOnlyList<string> CreateTRA_File(GameInfo info, string filename, Dictionary<string, string> entryList) =>
            TraFile.Write(filename, new TranslationDocument { Options = info.ToTranslationOptions(), Entries = entryList });
    }

    public static class Extraction
    {
        /// <summary>
        /// Extracts all translatable texts from an AGS game and writes them to a TRS file
        /// (default: next to the game file, with extension .trs).
        /// </summary>
        public static ExtractionResult ParseAGSFile(string filename, string? outputPath = null)
        {
            ExtractionResult result = TextExtractor.Extract(filename);
            string outPath = outputPath ?? Path.ChangeExtension(filename, ".trs");
            result.Warnings.AddRange(TrsFile.Write(outPath, result.ToTranslationDocument()));
            return result;
        }
    }
}
