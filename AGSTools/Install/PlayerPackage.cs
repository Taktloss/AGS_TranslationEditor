using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace AGSTools
{
    /// <summary>
    /// Builds a zip for players: the TRA, optional fonts, install/uninstall scripts for Linux/macOS and
    /// Windows, and a read-me. Players unpack it into the game folder and run the install script, which
    /// backs up every file it replaces.
    /// </summary>
    public static class PlayerPackage
    {
        /// <param name="gamePath">Game the TRA is for; used for checks and the save folder name.</param>
        /// <param name="readmeNote">Text from the translator put at the top of the read-me (credits, version, contact).</param>
        /// <returns>Warnings, e.g. a TRA for another game version.</returns>
        /// <exception cref="IOException">The TRA or a font cannot be used.</exception>
        public static List<string> Create(string gamePath, string traPath, IEnumerable<string>? fontFiles, string zipPath, string? readmeNote = null)
        {
            GameInfo info = GameInfo.GetGameInfo(gamePath) ?? throw new IOException($"No AGS game data found in '{gamePath}'.");
            string language = Path.GetFileNameWithoutExtension(traPath);
            if (!Path.GetExtension(traPath).Equals(".tra", StringComparison.OrdinalIgnoreCase))
                throw new IOException($"'{traPath}' is not a .tra file. Create the TRA first (trs2tra).");
            if (language.Any(c => c is ' ' or '"' or '\'' or '%' or '&' or '$' or '`' or '\\' or '!'))
                throw new IOException($"The TRA name \"{language}\" contains spaces or special characters; rename it, e.g. to \"{new string(language.Where(char.IsLetterOrDigit).ToArray())}.tra\".");

            var warnings = new List<string>();
            TranslationDocument tra = TraFile.Read(traPath, info.TextCodepage > 0 && !info.IsUnicode ? info.TextCodepage : AgsTextEncoding.DefaultAnsiCodepage);
            if (tra.Options.GameUid != info.UniqueId || !string.Equals(tra.Options.GameName, info.GameTitle, StringComparison.Ordinal))
                warnings.Add($"The TRA was made for \"{tra.Options.GameName}\" (ID {tra.Options.GameUid}), not for \"{info.GameTitle}\" (ID {info.UniqueId}); the game will not load it.");
            if (tra.Options.IsUtf8 && !info.IsUnicode)
                warnings.Add("The TRA is UTF-8 but the game has 8-bit texts: players on Linux and macOS will see English. Use an 8-bit TRA (--8bit).");

            var fonts = (fontFiles ?? Array.Empty<string>()).ToList();
            foreach (string font in fonts)
            {
                if (GameFonts.FontNumber(font) < 0)
                    throw new IOException($"'{Path.GetFileName(font)}' is not a game font name (agsfntN.ttf or agsfntN.wfn).");
                if (!File.Exists(font))
                    throw new IOException($"Font file '{font}' not found.");
            }
            if (fonts.Count > 0)
                warnings.Add("The package contains game fonts. They belong to the game's makers; make sure you may pass them on.");

            // Without it the Linux script only changes the game folder's acsetup.cfg
            string saveFolder = TranslationInstaller.ReadSaveFolder(gamePath);

            var names = new[] { language + ".tra" }.Concat(fonts.Select(f => Path.GetFileName(f).ToLowerInvariant())).ToList();
            string folder = language + "-Patch";
            AtomicFile.Write(zipPath, stream =>
            {
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                AddFile(zip, $"{folder}/{language}.tra", File.ReadAllBytes(traPath));
                foreach (string font in fonts)
                    AddFile(zip, $"{folder}/{Path.GetFileName(font).ToLowerInvariant()}", File.ReadAllBytes(font));

                string Fill(string template) => template
                    .Replace("@LANG@", language)
                    .Replace("@FILES@", string.Join(" ", names))
                    .Replace("@SAVEFOLDER@", saveFolder.Replace("\"", ""))
                    .Replace("@GAME@", info.GameTitle);
                AddText(zip, $"{folder}/install.sh", Fill(InstallSh), executable: true);
                AddText(zip, $"{folder}/uninstall.sh", Fill(UninstallSh), executable: true);
                AddText(zip, $"{folder}/install.bat", Fill(InstallBat).Replace("\n", "\r\n"));
                AddText(zip, $"{folder}/uninstall.bat", Fill(UninstallBat).Replace("\n", "\r\n"));
                string note = string.IsNullOrWhiteSpace(readmeNote) ? "" : readmeNote.Trim() + "\n\n";
                AddText(zip, $"{folder}/LIESMICH.txt", (note + Fill(Readme)).Replace("\n", "\r\n"));
            });
            return warnings;
        }

        private static void AddFile(ZipArchive zip, string name, byte[] data, bool executable = false)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            // Unix permissions in the upper 16 bits: rwxr-xr-x for scripts, rw-r--r-- otherwise
            entry.ExternalAttributes = (executable ? 0x81ED : 0x81A4) << 16;
            using Stream s = entry.Open();
            s.Write(data);
        }

        private static void AddText(ZipArchive zip, string name, string text, bool executable = false) =>
            AddFile(zip, name, new UTF8Encoding(false).GetBytes(text), executable);

        private const string InstallSh = """
            #!/bin/sh
            # Installs the @LANG@ translation of "@GAME@".
            # Game folder: the folder above this one, or the one given as argument.
            # Every replaced file is backed up in "@LANG@-backup" in the game folder; uninstall.sh undoes it.
            set -e
            here=$(cd "$(dirname "$0")" && pwd)
            game=${1:-$(dirname "$here")}
            lang=@LANG@
            files="@FILES@"

            found=
            for g in "$game"/*.ags "$game"/*.exe "$game"/game28.dta "$game"/ac2game.dta; do
                [ -f "$g" ] && found=1
            done
            if [ -z "$found" ]; then
                echo "No AGS game found in: $game"
                echo "Unpack this folder into the game folder, or run: sh install.sh /path/to/game"
                exit 1
            fi
            backup="$game/$lang-backup"
            mkdir -p "$backup"
            touch "$backup/added.txt" "$backup/configs.txt"

            for f in $files; do
                if [ -e "$game/$f" ]; then
                    # Keep the first backup: it holds the game's original file
                    [ -e "$backup/$f" ] || grep -qxF "$f" "$backup/added.txt" || cp -p "$game/$f" "$backup/$f"
                else
                    grep -qxF "$f" "$backup/added.txt" || echo "$f" >> "$backup/added.txt"
                fi
                cp "$here/$f" "$game/$f"
                echo "installed $f"
            done

            # Select the translation in acsetup.cfg (the game folder's and the engine's per-user copy)
            set_translation() {
                cfg=$1
                n=$(grep -c . "$backup/configs.txt" || true)
                if ! cut -f2 "$backup/configs.txt" | grep -qxF "$cfg"; then
                    if [ -f "$cfg" ]; then cp -p "$cfg" "$backup/config$n.cfg"; printf 'config%s.cfg\t%s\n' "$n" "$cfg" >> "$backup/configs.txt"
                    else printf -- '-\t%s\n' "$cfg" >> "$backup/configs.txt"; fi
                fi
                { cat "$cfg" 2>/dev/null || true; } | awk -v lang="$lang" '
                    { sub(/\r$/, "") }
                    /^[ \t]*\[/ { if (insec && !done) { print "translation=" lang; done = 1 }
                                  s = tolower($0); gsub(/[][ \t]/, "", s); insec = (s == "language"); print; next }
                    insec && /^[ \t]*translation[ \t]*=/ { if (!done) print "translation=" lang; done = 1; next }
                    { print }
                    END { if (!done) { if (!insec) print "[language]"; print "translation=" lang } }' > "$cfg.new"
                mv "$cfg.new" "$cfg"
                echo "language set in $cfg"
            }
            set_translation "$game/acsetup.cfg"
            user_cfg="${XDG_DATA_HOME:-$HOME/.local/share}/ags/@SAVEFOLDER@/acsetup.cfg"
            if [ -n "@SAVEFOLDER@" ] && [ -f "$user_cfg" ]; then set_translation "$user_cfg"; fi

            echo
            echo "Done. Start the game as usual. To undo: sh \"$here/uninstall.sh\""
            """;

        private const string UninstallSh = """
            #!/bin/sh
            # Undoes install.sh: puts the backed-up files back. Files the install added are moved into
            # "@LANG@-backup/removed" (nothing is deleted).
            set -e
            here=$(cd "$(dirname "$0")" && pwd)
            game=${1:-$(dirname "$here")}
            lang=@LANG@
            files="@FILES@"
            backup="$game/$lang-backup"
            if [ ! -d "$backup" ]; then
                echo "No backup found in $game - the translation was not installed with install.sh."
                exit 1
            fi
            mkdir -p "$backup/removed"

            for f in $files; do
                if [ -e "$backup/$f" ]; then
                    [ -e "$game/$f" ] && mv "$game/$f" "$backup/removed/$f"
                    cp -p "$backup/$f" "$game/$f"
                    echo "restored $f"
                elif grep -qxF "$f" "$backup/added.txt" 2>/dev/null && [ -e "$game/$f" ]; then
                    mv "$game/$f" "$backup/removed/$f"
                    echo "removed $f"
                fi
            done

            n=0
            while IFS="$(printf '\t')" read -r copy cfg; do
                [ -n "$cfg" ] || continue
                n=$((n + 1))
                if [ "$copy" = "-" ]; then
                    [ -e "$cfg" ] && mv "$cfg" "$backup/removed/acsetup$n.cfg"
                else
                    [ -e "$cfg" ] && mv "$cfg" "$backup/removed/acsetup$n.cfg"
                    cp -p "$backup/$copy" "$cfg"
                fi
                echo "restored $cfg"
            done < "$backup/configs.txt"

            mv "$backup" "$backup-undone-$(date +%Y%m%d-%H%M%S)"
            echo
            echo "Done. The game is back as it was before the install."
            """;

        private const string InstallBat = """
            @echo off
            rem Installs the @LANG@ translation of "@GAME@".
            rem Game folder: the folder above this one, or the one given as argument.
            rem Every replaced file is backed up in "@LANG@-backup" in the game folder; uninstall.bat undoes it.
            setlocal
            set "HERE=%~dp0"
            if "%~1"=="" (for %%I in ("%HERE%..") do set "GAME=%%~fI") else set "GAME=%~1"
            set "BACKUP=%GAME%\@LANG@-backup"

            dir /b "%GAME%\*.exe" "%GAME%\*.ags" "%GAME%\game28.dta" >nul 2>&1
            if errorlevel 1 (
                echo No AGS game found in: %GAME%
                echo Unpack this folder into the game folder, or run: install.bat "C:\path\to\game"
                pause
                exit /b 1
            )
            if not exist "%BACKUP%" mkdir "%BACKUP%"
            if not exist "%BACKUP%\added.txt" type nul > "%BACKUP%\added.txt"

            for %%F in (@FILES@) do call :install %%F
            echo.
            echo Done. Choose "@LANG@" as language in the game's setup
            echo (winsetup.exe, or start the game exe with --setup), then start the game.
            echo To undo: uninstall.bat
            pause
            exit /b 0

            :install
            if exist "%GAME%\%1" (
                findstr /x /c:"%1" "%BACKUP%\added.txt" >nul || if not exist "%BACKUP%\%1" copy /y "%GAME%\%1" "%BACKUP%\%1" >nul
            ) else (
                findstr /x /c:"%1" "%BACKUP%\added.txt" >nul || echo %1>> "%BACKUP%\added.txt"
            )
            copy /y "%HERE%%1" "%GAME%\%1" >nul
            echo installed %1
            exit /b 0
            """;

        private const string UninstallBat = """
            @echo off
            rem Undoes install.bat: puts the backed-up files back. Files the install added are moved into
            rem "@LANG@-backup\removed" (nothing is deleted).
            setlocal
            set "HERE=%~dp0"
            if "%~1"=="" (for %%I in ("%HERE%..") do set "GAME=%%~fI") else set "GAME=%~1"
            set "BACKUP=%GAME%\@LANG@-backup"
            if not exist "%BACKUP%" (
                echo No backup found in %GAME% - the translation was not installed with install.bat.
                pause
                exit /b 1
            )
            if not exist "%BACKUP%\removed" mkdir "%BACKUP%\removed"
            for %%F in (@FILES@) do call :restore %%F
            echo.
            echo Done. The game files are back as they were before the install.
            echo Choose the original language in the game's setup again if needed.
            pause
            exit /b 0

            :restore
            if exist "%BACKUP%\%1" (
                if exist "%GAME%\%1" move /y "%GAME%\%1" "%BACKUP%\removed\%1" >nul
                copy /y "%BACKUP%\%1" "%GAME%\%1" >nul
                echo restored %1
                exit /b 0
            )
            findstr /x /c:"%1" "%BACKUP%\added.txt" >nul
            if not errorlevel 1 if exist "%GAME%\%1" (
                move /y "%GAME%\%1" "%BACKUP%\removed\%1" >nul
                echo removed %1
            )
            exit /b 0
            """;

        private const string Readme = """
            @LANG@-Übersetzung für "@GAME@"
            ===================================

            Installation
            ------------
            1. Diesen Ordner (@LANG@-Patch) in den Spielordner entpacken, also dorthin, wo die
               .exe bzw. .ags-Datei des Spiels liegt.
            2. Installationsskript im Ordner @LANG@-Patch starten:
               - Windows: install.bat doppelklicken. Danach im Setup des Spiels (winsetup.exe oder
                 Spiel-exe mit --setup) als Sprache "@LANG@" wählen.
               - Linux/macOS: im Terminal  sh install.sh  ausführen. Die Sprache wird automatisch
                 eingestellt.
            3. Spiel normal starten.

            Ersetzte Dateien werden vorher im Spielordner unter "@LANG@-backup" gesichert.

            Ohne Skript: die Dateien @FILES@ in den Spielordner kopieren und in der acsetup.cfg
            unter [language] die Zeile  translation=@LANG@  setzen (bzw. im Setup die Sprache wählen).

            Deinstallation
            --------------
            uninstall.bat (Windows) bzw.  sh uninstall.sh  (Linux/macOS) ausführen.
            Die gesicherten Originaldateien kommen zurück; nichts wird gelöscht.

            Hinweise
            --------
            - Nach einem Spiel-Update (z.B. durch Steam) kann die Übersetzung verschwinden oder nicht
              mehr passen. Dann das Installationsskript erneut ausführen bzw. eine neue Version der
              Übersetzung besorgen.
            - Die Übersetzung passt nur zur Spielversion, für die sie erstellt wurde.

            -----------------------------------------------------------------------------------------
            English: unpack this folder into the game folder and run install.bat (Windows; then pick
            "@LANG@" in the game's setup) or "sh install.sh" (Linux/macOS). Undo with uninstall.
            """;
    }
}
