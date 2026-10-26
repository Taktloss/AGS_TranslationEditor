using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AGSTools
{
    public sealed class InstallResult
    {
        public string GameFolder { get; internal set; } = "";

        /// <summary>Name the game uses for the translation (TRA file name without extension).</summary>
        public string Language { get; internal set; } = "";

        /// <summary>Folder with the replaced files and the list of changes, for <see cref="TranslationInstaller.Uninstall"/>.</summary>
        public string BackupFolder { get; internal set; } = "";

        public List<string> Added { get; } = new();
        public List<string> Replaced { get; } = new();

        /// <summary>acsetup.cfg files whose translation setting was changed.</summary>
        public List<string> ConfigFiles { get; } = new();

        public List<string> Warnings { get; } = new();
    }

    public sealed class UninstallResult
    {
        public string BackupFolder { get; internal set; } = "";

        /// <summary>Files put back from the backup (game files and acsetup.cfg).</summary>
        public List<string> Restored { get; } = new();

        /// <summary>Files the install had added; they are moved into the backup's "removed" folder, not deleted.</summary>
        public List<string> Removed { get; } = new();

        public List<string> Warnings { get; } = new();
    }

    /// <summary>
    /// Puts a translation (TRA and optional fonts) into a game folder and selects it in acsetup.cfg.
    /// Every file that gets replaced is backed up first; <see cref="Uninstall"/> undoes the last install.
    /// Nothing is ever deleted: files an install added are moved into the backup folder on uninstall.
    /// </summary>
    public static class TranslationInstaller
    {
        public const string BackupFolderName = "agstranslate-backup";
        private const string ManifestName = "manifest.txt";
        private const string UndoneMarker = "uninstalled.txt";
        private const string ConfigName = "acsetup.cfg";

        /// <param name="gamePath">Game exe, .ags or game28.dta.</param>
        /// <param name="traPath">Compiled translation; its file name becomes the language name.</param>
        /// <param name="fontFiles">Font files named agsfntN.ttf/.wfn to put next to the game.</param>
        /// <param name="force">Install even if the TRA was made for another game or version.</param>
        /// <param name="userConfigFolders">Folders to look for a per-user acsetup.cfg in; null for the engine's default places.</param>
        /// <exception cref="IOException">The game, TRA or a font cannot be used; nothing was changed.</exception>
        public static InstallResult Install(string gamePath, string traPath, IEnumerable<string>? fontFiles = null,
            bool force = false, IEnumerable<string>? userConfigFolders = null)
        {
            GameInfo info = GameInfo.GetGameInfo(gamePath) ?? throw new IOException($"No AGS game data found in '{gamePath}'.");
            string gameFolder = Path.GetDirectoryName(Path.GetFullPath(info.DataPath))!;
            string language = Path.GetFileNameWithoutExtension(traPath);
            if (!Path.GetExtension(traPath).Equals(".tra", StringComparison.OrdinalIgnoreCase))
                throw new IOException($"'{traPath}' is not a .tra file. Create the TRA first (trs2tra).");

            TranslationDocument tra = TraFile.Read(traPath, info.TextCodepage > 0 && !info.IsUnicode ? info.TextCodepage : AgsTextEncoding.DefaultAnsiCodepage);
            if (!force && (tra.Options.GameUid != info.UniqueId || !string.Equals(tra.Options.GameName, info.GameTitle, StringComparison.Ordinal)))
                throw new IOException($"'{Path.GetFileName(traPath)}' was made for \"{tra.Options.GameName}\" (ID {tra.Options.GameUid}), "
                    + $"but the game is \"{info.GameTitle}\" (ID {info.UniqueId}). The game would not load it; create the TRA again with this game file.");

            var files = new List<string> { Path.GetFullPath(traPath) };
            foreach (string font in fontFiles ?? Array.Empty<string>())
            {
                if (GameFonts.FontNumber(font) < 0)
                    throw new IOException($"'{Path.GetFileName(font)}' is not a game font name (agsfntN.ttf or agsfntN.wfn).");
                if (!File.Exists(font))
                    throw new IOException($"Font file '{font}' not found.");
                files.Add(Path.GetFullPath(font));
            }
            if (files.Any(f => Path.GetDirectoryName(f)!.Equals(gameFolder, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("The files to install are already in the game folder. Install from another folder, e.g. your work folder.");

            // Writing through a link would change the file it points to, e.g. in another game folder
            foreach (string name in files.Select(Path.GetFileName).Append(ConfigName))
                if (ExistingName(gameFolder, name!) is { } existing && new FileInfo(Path.Combine(gameFolder, existing)).LinkTarget != null)
                    throw new IOException($"'{existing}' in the game folder is a link; refusing to write through it.");

            var result = new InstallResult { GameFolder = gameFolder, Language = language };
            string backup = CreateBackupFolder(gameFolder);
            result.BackupFolder = backup;
            var manifest = new List<string>
            {
                $"# AGSTranslate install {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}",
                $"language\t{language}",
            };

            foreach (string source in files)
            {
                // Keep the spelling of an existing file, so no second file differing only in case appears
                string name = ExistingName(gameFolder, Path.GetFileName(source)) ?? Path.GetFileName(source);
                string target = Path.Combine(gameFolder, name);
                if (File.Exists(target))
                {
                    File.Copy(target, Path.Combine(backup, name));
                    manifest.Add($"replaced\t{name}");
                    result.Replaced.Add(name);
                }
                else
                {
                    manifest.Add($"added\t{name}");
                    result.Added.Add(name);
                }
                WriteManifest(backup, manifest);
                File.Copy(source, target, overwrite: true);
            }

            var configs = new List<string> { Path.Combine(gameFolder, ConfigName) };
            string saveFolder = ReadSaveFolder(gamePath);
            if (saveFolder.Length > 0)
                configs.AddRange((userConfigFolders ?? DefaultUserConfigFolders())
                    .Select(dir => Path.Combine(dir, saveFolder, ConfigName))
                    .Where(File.Exists));
            for (int i = 0; i < configs.Count; i++)
            {
                string config = configs[i];
                if (File.Exists(config))
                {
                    string copy = $"config{i}.cfg";
                    File.Copy(config, Path.Combine(backup, copy));
                    manifest.Add($"config\t{copy}\t{config}");
                }
                else
                {
                    manifest.Add($"config-added\t{config}");
                }
                WriteManifest(backup, manifest);
                IniFile.SetValue(config, "language", "translation", language);
                result.ConfigFiles.Add(config);
            }

            CheckFonts(gamePath, result.Added.Concat(result.Replaced).Where(n => GameFonts.FontNumber(n) >= 0)
                .Select(n => Path.Combine(gameFolder, n)), result.Warnings);
            if (info.IsUnicode != tra.Options.IsUtf8 && tra.Options.IsUtf8)
                result.Warnings.Add("The TRA is UTF-8 but the game has 8-bit texts: it only works on Windows. Create an 8-bit TRA (--8bit) for Linux and macOS.");
            return result;
        }

        /// <summary>Undoes the most recent install that was not undone yet.</summary>
        /// <exception cref="IOException">There is no install to undo.</exception>
        public static UninstallResult Uninstall(string gamePath)
        {
            GameInfo info = GameInfo.GetGameInfo(gamePath) ?? throw new IOException($"No AGS game data found in '{gamePath}'.");
            string gameFolder = Path.GetDirectoryName(Path.GetFullPath(info.DataPath))!;
            string backup = FindBackups(gameFolder).FirstOrDefault()
                ?? throw new IOException($"No translation installed by AGSTranslate was found in '{gameFolder}'.");

            var result = new UninstallResult { BackupFolder = backup };
            string removed = Path.Combine(backup, "removed");
            foreach (string line in File.ReadAllLines(Path.Combine(backup, ManifestName)).Reverse())
            {
                string[] parts = line.Split('\t');
                switch (parts[0])
                {
                    case "added" when parts.Length == 2:
                        MoveAway(Path.Combine(gameFolder, parts[1]), removed, result);
                        break;
                    case "replaced" when parts.Length == 2:
                        Restore(Path.Combine(backup, parts[1]), Path.Combine(gameFolder, parts[1]), removed, result);
                        break;
                    case "config" when parts.Length == 3:
                        Restore(Path.Combine(backup, parts[1]), parts[2], removed, result);
                        break;
                    case "config-added" when parts.Length == 2:
                        MoveAway(parts[1], removed, result);
                        break;
                }
            }
            File.WriteAllText(Path.Combine(backup, UndoneMarker), $"Uninstalled {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}\n");
            return result;
        }

        /// <summary>Backup folders of installs that can still be undone, newest first.</summary>
        public static List<string> FindBackups(string gameFolder)
        {
            string root = Path.Combine(gameFolder, BackupFolderName);
            if (!Directory.Exists(root))
                return new List<string>();
            return Directory.GetDirectories(root)
                .Where(d => File.Exists(Path.Combine(d, ManifestName)) && !File.Exists(Path.Combine(d, UndoneMarker)))
                .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Where the engine keeps the per-user acsetup.cfg (one subfolder per game). It writes the chosen
        /// translation there when the game quits, and that setting wins over the game folder's acsetup.cfg.
        /// </summary>
        public static IEnumerable<string> DefaultUserConfigFolders()
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsWindows())
                yield return Path.Combine(home, "Saved Games");
            else if (!OperatingSystem.IsMacOS())
                yield return Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".local", "share") : xdg, "ags");
        }

        private static string CreateBackupFolder(string gameFolder)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(gameFolder, BackupFolderName, stamp);
            for (int n = 2; Directory.Exists(path); n++)
                path = Path.Combine(gameFolder, BackupFolderName, $"{stamp}-{n}");
            Directory.CreateDirectory(path);
            return path;
        }

        private static void WriteManifest(string backup, List<string> lines) =>
            File.WriteAllLines(Path.Combine(backup, ManifestName), lines);

        private static string? ExistingName(string folder, string name) =>
            Directory.EnumerateFiles(folder).Select(Path.GetFileName)
                .FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The game's save folder name, or "" if the game data cannot be read that far.</summary>
        internal static string ReadSaveFolder(string gamePath)
        {
            try
            {
                byte[] data = GameFileSource.Open(gamePath).ReadMainGameData();
                try
                {
                    return GameDataReader.Read(data).SaveFolder.Trim();
                }
                catch (AgsFormatException)
                {
                    // Old formats: 3.6.1+ games also keep the name in the header extensions
                    return GameDataReader.ReadHeader(data).SaveFolder.Trim();
                }
            }
            catch (Exception ex) when (ex is AgsFormatException or IOException)
            {
                return "";
            }
        }

        // A loose agsfntN.wfn is not used when the game has an agsfntN.ttf, loose or packaged
        private static void CheckFonts(string gamePath, IEnumerable<string> installedFonts, List<string> warnings)
        {
            var installed = installedFonts.ToList();
            if (installed.Count == 0)
                return;
            List<GameFont> loaded;
            try
            {
                loaded = GameFonts.Load(gamePath);
            }
            catch (IOException)
            {
                return;
            }
            foreach (string path in installed)
            {
                GameFont? font = loaded.FirstOrDefault(f => f.Number == GameFonts.FontNumber(path));
                if (font != null && !string.Equals(font.FilePath, path, StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"Font {font.Number}: the game uses {font.FileName}"
                        + $"{(font.Origin == FontOrigin.Package ? " from its package" : "")} instead of the installed {Path.GetFileName(path)}.");
            }
        }

        private static void Restore(string backupCopy, string target, string removed, UninstallResult result)
        {
            if (!File.Exists(backupCopy))
            {
                result.Warnings.Add($"Backup copy '{backupCopy}' is missing; '{target}' was left as it is.");
                return;
            }
            if (File.Exists(target))
                MoveIntoRemoved(target, removed);
            File.Copy(backupCopy, target);
            result.Restored.Add(target);
        }

        private static void MoveAway(string path, string removed, UninstallResult result)
        {
            if (!File.Exists(path))
                return;
            MoveIntoRemoved(path, removed);
            result.Removed.Add(path);
        }

        private static void MoveIntoRemoved(string path, string removed)
        {
            Directory.CreateDirectory(removed);
            string target = Path.Combine(removed, Path.GetFileName(path));
            for (int n = 2; File.Exists(target); n++)
                target = Path.Combine(removed, $"{Path.GetFileNameWithoutExtension(path)}.{n}{Path.GetExtension(path)}");
            File.Move(path, target);
        }
    }
}
