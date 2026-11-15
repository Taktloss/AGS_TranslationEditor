using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AGSFontEditor.Fonts;
using AGSFontEditor.Localization;
using AGSFontEditor.Rendering;
using AGSTools;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AGSFontEditor.ViewModels
{
    /// <summary>One sprite in the list, with a small preview made when it is first shown.</summary>
    public sealed partial class SpriteItemViewModel : ObservableObject
    {
        private readonly SpriteEditorViewModel _owner;
        private Bitmap? _thumbnail;

        internal SpriteItemViewModel(SpriteEditorViewModel owner, SpriteEntry entry)
        {
            _owner = owner;
            Number = entry.Number;
            Width = entry.Width;
            Height = entry.Height;
        }

        public int Number { get; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        [ObservableProperty]
        private bool _isChanged;

        public string Label => $"{Number}";
        public string SizeText => $"{Width} × {Height}";

        public Bitmap? Thumbnail => _thumbnail ??= _owner.MakeThumbnail(Number);

        internal void Refresh(SpriteImage image)
        {
            Width = image.Width;
            Height = image.Height;
            _thumbnail = null;
            OnPropertyChanged(nameof(Thumbnail));
            OnPropertyChanged(nameof(SizeText));
        }
    }

    /// <summary>A game font to write text with.</summary>
    public sealed record SpriteFontChoice(string Name, EditableFont Font)
    {
        public override string ToString() => Name;
    }

    /// <summary>
    /// Sprite (image) editor: browse a game's sprites, export them as PNG, replace them with edited PNGs or
    /// edit them here (fill an area, write text with a game font), and build a sprite patch for the game.
    /// </summary>
    public sealed partial class SpriteEditorViewModel : ObservableObject, IDisposable
    {
        private const int ThumbnailSize = 48;
        private const int MaxUndo = 30;

        private GameSprites? _sprites;
        private readonly List<SpriteItemViewModel> _all = new();
        private readonly Dictionary<int, SpriteImage> _edited = new();
        private readonly Dictionary<int, Stack<SpriteImage>> _undo = new();
        private TranslationOptions _encoding = new() { AnsiCodepage = 1252 };

        public ObservableCollection<SpriteItemViewModel> Items { get; } = new();
        public ObservableCollection<SpriteFontChoice> Fonts { get; } = new();
        public IReadOnlyList<int> ZoomLevels { get; } = new[] { 1, 2, 3, 4, 6, 8 };
        public IReadOnlyList<string> AlignNames { get; } = new[] { Loc.T("Left"), Loc.T("Centered"), Loc.T("Right") };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelection))]
        [NotifyPropertyChangedFor(nameof(SelectedInfo))]
        private SpriteItemViewModel? _selectedItem;

        [ObservableProperty]
        private string _filterText = "";

        [ObservableProperty]
        private bool _onlyLarge = true;

        [ObservableProperty]
        private bool _onlyChanged;

        [ObservableProperty]
        private Bitmap? _image;

        [ObservableProperty]
        private int _zoom = 2;

        [ObservableProperty]
        private bool _showOriginal;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectionText))]
        private PixelRect? _selection;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ColorBrush))]
        private string _colorText = "#FFFFFF";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(OutlineBrush))]
        private string _outlineText = "#000000";

        [ObservableProperty]
        private bool _useOutline;

        [ObservableProperty]
        private string _textToDraw = "";

        [ObservableProperty]
        private SpriteFontChoice? _selectedFont;

        [ObservableProperty]
        private int _alignIndex = 1;

        [ObservableProperty]
        private string _statusText = Loc.T("Open a game to see its sprites.");

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsIdle))]
        private bool _isBusy;

        [ObservableProperty]
        private string _countText = "";

        public bool IsIdle => !IsBusy;
        public bool HasGame => _sprites != null;
        public bool HasSelection => SelectedItem != null;
        public bool HasChanges => _edited.Count > 0;

        /// <summary>True after a sprite patch was written, until the next change.</summary>
        public bool PatchIsCurrent { get; private set; }
        public string? GamePath => _sprites?.GamePath;

        public string SelectedInfo
        {
            get
            {
                if (SelectedItem is not { } item || _sprites?.File[item.Number] is not { } e)
                    return "";
                string alpha = _sprites.HasAlpha(item.Number) ? Loc.T(", alpha channel") : "";
                string changed = _edited.TryGetValue(item.Number, out var img)
                    ? Loc.F(" — changed ({0} × {1})", img.Width, img.Height) : "";
                return Loc.F("Sprite {0}: {1} × {2}, {3}-bit{4}", item.Number, e.Width, e.Height, e.BytesPerPixel * 8, alpha) + changed;
            }
        }

        public string SelectionText => Selection is { } s && s.Width > 0
            ? Loc.F("Selection: {0}, {1} – {2} × {3}", s.X, s.Y, s.Width, s.Height)
            : Loc.T("No selection: drag over the image to select an area.");

        public Avalonia.Media.IBrush ColorBrush => new Avalonia.Media.SolidColorBrush(ParseColor(ColorText) is uint c ? Avalonia.Media.Color.FromUInt32(c) : Avalonia.Media.Colors.Transparent);
        public Avalonia.Media.IBrush OutlineBrush => new Avalonia.Media.SolidColorBrush(ParseColor(OutlineText) is uint c ? Avalonia.Media.Color.FromUInt32(c) : Avalonia.Media.Colors.Transparent);

        // ── Loading ──

        public async Task LoadGameAsync(string gamePath)
        {
            IsBusy = true;
            StatusText = Loc.F("Loading sprites of {0}...", Path.GetFileName(gamePath));
            try
            {
                var (sprites, fonts) = await Task.Run(() =>
                {
                    GameSprites s = GameSprites.Open(gamePath);
                    var f = new List<SpriteFontChoice>();
                    try
                    {
                        foreach (GameFont font in GameFonts.Load(gamePath).Where(x => x.Error == null))
                        {
                            try { f.Add(new SpriteFontChoice(Loc.F("Font {0} ({1})", font.Number, font.FileName), EditableFont.Open(font))); }
                            catch (Exception ex) when (ex is IOException or AgsFormatException or ArgumentException) { }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or AgsFormatException) { }
                    return (s, f);
                });
                Close();
                _sprites = sprites;
                GameData game = sprites.Game;
                _encoding = game.IsUnicode
                    ? new TranslationOptions { Encoding = "UTF-8" }
                    : new TranslationOptions { AnsiCodepage = game.TextCodepage > 0 ? game.TextCodepage : 1252 };
                foreach (SpriteFontChoice f in fonts)
                    Fonts.Add(f);
                SelectedFont = Fonts.FirstOrDefault();
                foreach (SpriteEntry e in sprites.File.Sprites)
                    _all.Add(new SpriteItemViewModel(this, e));
                ApplyFilter();
                OnPropertyChanged(nameof(HasGame));
                OnPropertyChanged(nameof(GamePath));
                StatusText = Loc.F("{0} sprites in {1}.", _all.Count, Path.GetFileName(gamePath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AgsFormatException)
            {
                StatusText = Loc.F("Could not load the sprites: {0}", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Close()
        {
            SelectedItem = null;
            Items.Clear();
            _all.Clear();
            _edited.Clear();
            _undo.Clear();
            foreach (SpriteFontChoice f in Fonts)
                f.Font.Dispose();
            Fonts.Clear();
            _sprites?.Dispose();
            _sprites = null;
            PatchIsCurrent = false;
            OnPropertyChanged(nameof(HasChanges));
        }

        public void Dispose() => Close();

        /// <summary>True if <paramref name="folder"/> is the folder of the loaded game.</summary>
        public bool IsGameFolder(string folder) =>
            _sprites != null && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)),
                Path.GetDirectoryName(Path.GetFullPath(_sprites.SourcePath)), StringComparison.OrdinalIgnoreCase);

        // ── List ──

        partial void OnFilterTextChanged(string value) => ApplyFilter();
        partial void OnOnlyLargeChanged(bool value) => ApplyFilter();
        partial void OnOnlyChangedChanged(bool value) => ApplyFilter();

        /// <summary>Filter: a number or range ("120", "100-200") or a minimum size ("300x200").</summary>
        private void ApplyFilter()
        {
            SpriteItemViewModel? selected = SelectedItem;
            string f = (FilterText ?? "").Trim().ToLowerInvariant();
            Func<SpriteItemViewModel, bool> match = _ => true;
            string[] size = f.Split('x');
            string[] range = f.Split('-');
            if (size.Length == 2 && int.TryParse(size[0], out int minW) && int.TryParse(size[1], out int minH))
                match = i => i.Width >= minW && i.Height >= minH;
            else if (range.Length == 2 && int.TryParse(range[0], out int from) && int.TryParse(range[1], out int to))
                match = i => i.Number >= from && i.Number <= to;
            else if (int.TryParse(f, out int n))
                match = i => i.Number.ToString().StartsWith(n.ToString(), StringComparison.Ordinal);

            Items.Clear();
            foreach (SpriteItemViewModel item in _all)
            {
                if (OnlyLarge && item.Width * item.Height < 32 * 32 && !item.IsChanged)
                    continue;
                if (OnlyChanged && !item.IsChanged)
                    continue;
                if (match(item))
                    Items.Add(item);
            }
            CountText = Loc.F("{0} of {1} sprites", Items.Count, _all.Count);
            if (selected != null && Items.Contains(selected))
                SelectedItem = selected;
        }

        internal Bitmap? MakeThumbnail(int number)
        {
            try
            {
                SpriteImage image = Current(number);
                int scale = Math.Max(1, (int)Math.Ceiling(Math.Max(image.Width, image.Height) / (double)ThumbnailSize));
                var small = new SpriteImage(Math.Max(1, image.Width / scale), Math.Max(1, image.Height / scale));
                for (int y = 0; y < small.Height; y++)
                    for (int x = 0; x < small.Width; x++)
                        small[x, y] = image[x * scale, y * scale];
                return ToBitmap(small);
            }
            catch (Exception ex) when (ex is IOException or AgsFormatException)
            {
                return null;
            }
        }

        // ── Selected sprite ──

        partial void OnSelectedItemChanged(SpriteItemViewModel? value)
        {
            Selection = null;
            ShowImage();
        }

        partial void OnShowOriginalChanged(bool value) => ShowImage();

        /// <summary>The edited image of a sprite, or the game's one.</summary>
        private SpriteImage Current(int number) =>
            _edited.TryGetValue(number, out SpriteImage? image) ? image : _sprites!.Read(number);

        private void ShowImage()
        {
            if (SelectedItem is not { } item || _sprites == null)
            {
                Image = null;
                return;
            }
            try
            {
                Image = ToBitmap(ShowOriginal ? _sprites.Read(item.Number) : Current(item.Number));
            }
            catch (Exception ex) when (ex is IOException or AgsFormatException)
            {
                Image = null;
                StatusText = Loc.F("Sprite {0} cannot be shown: {1}", item.Number, ex.Message);
            }
            OnPropertyChanged(nameof(SelectedInfo));
        }

        private static WriteableBitmap ToBitmap(SpriteImage image)
        {
            var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using ILockedFramebuffer fb = bitmap.Lock();
            int[] row = new int[image.Width];
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                    row[x] = unchecked((int)image.Pixels[y * image.Width + x]);
                Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
            }
            return bitmap;
        }

        // ── Editing ──

        /// <summary>Gives a copy of the current image to change; null (with a status text) if nothing is selected.</summary>
        private SpriteImage? BeginEdit()
        {
            if (SelectedItem is not { } item || _sprites == null)
                return null;
            SpriteImage current = Current(item.Number);
            if (!_undo.TryGetValue(item.Number, out var stack))
                _undo[item.Number] = stack = new Stack<SpriteImage>();
            stack.Push(current);
            if (stack.Count > MaxUndo)
            {
                var keep = stack.Take(MaxUndo).Reverse().ToList();
                stack.Clear();
                foreach (SpriteImage s in keep)
                    stack.Push(s);
            }
            return new SpriteImage(current.Width, current.Height, (uint[])current.Pixels.Clone());
        }

        private void CommitEdit(int number, SpriteImage image)
        {
            if (_sprites!.IsUnchanged(number, image) && _sprites.File[number] is { } e && e.Width == image.Width && e.Height == image.Height)
                _edited.Remove(number);
            else
                _edited[number] = image;
            SpriteItemViewModel? item = _all.FirstOrDefault(i => i.Number == number);
            if (item != null)
            {
                item.IsChanged = _edited.ContainsKey(number);
                item.Refresh(_edited.TryGetValue(number, out var shown) ? shown : _sprites.Read(number));
            }
            ShowOriginal = false;
            ShowImage();
            PatchIsCurrent = false;
            OnPropertyChanged(nameof(HasChanges));
        }

        /// <summary>Fills the selection with the current color (e.g. to remove text before writing new text).</summary>
        [RelayCommand]
        public void FillSelection()
        {
            if (Selection is not { } sel || sel.Width <= 0 || ParseColor(ColorText) is not uint color)
            {
                StatusText = Selection == null ? Loc.T("Select an area first.") : Loc.T("The color is not valid (use #RRGGBB or #AARRGGBB).");
                return;
            }
            if (BeginEdit() is not { } image)
                return;
            for (int y = Math.Max(0, sel.Y); y < Math.Min(image.Height, sel.Bottom); y++)
                for (int x = Math.Max(0, sel.X); x < Math.Min(image.Width, sel.Right); x++)
                    image[x, y] = color;
            CommitEdit(SelectedItem!.Number, image);
            StatusText = Loc.F("Filled {0} × {1} pixels.", sel.Width, sel.Height);
        }

        /// <summary>Writes the text with the selected font into the selection (or at the top left corner).</summary>
        [RelayCommand]
        public void DrawText()
        {
            if (SelectedFont is not { } font || string.IsNullOrEmpty(TextToDraw))
            {
                StatusText = SelectedFont == null ? Loc.T("The game has no font to write with.") : Loc.T("Enter a text first.");
                return;
            }
            if (ParseColor(ColorText) is not uint color || (UseOutline && ParseColor(OutlineText) is null))
            {
                StatusText = Loc.T("The color is not valid (use #RRGGBB or #AARRGGBB).");
                return;
            }
            if (BeginEdit() is not { } image)
                return;
            PixelRect area = Selection is { Width: > 0 } s ? s : new PixelRect(0, 0, image.Width, image.Height);
            var (_, textHeight) = SpriteText.Measure(font.Font, TextToDraw, _encoding);
            int y = area.Y + Math.Max(0, (area.Height - textHeight) / 2);
            List<string> missing = SpriteText.Draw(image, font.Font, TextToDraw, area.X, y, area.Width, (TextAlign)AlignIndex,
                color, UseOutline ? ParseColor(OutlineText) : null, _encoding);
            CommitEdit(SelectedItem!.Number, image);
            StatusText = missing.Count == 0
                ? Loc.T("Text written.")
                : Loc.T("Text written. The font has no glyph for: ") + string.Join(" ", missing);
        }

        /// <summary>Adds font files (e.g. fonts with the characters of the translation) to write with.</summary>
        public void AddFontFiles(IEnumerable<string> paths)
        {
            var errors = new List<string>();
            SpriteFontChoice? last = null;
            foreach (string path in paths)
            {
                try
                {
                    GameFont font = GameFont.FromFile(path);
                    last = new SpriteFontChoice(Loc.F("{0} (file)", Path.GetFileName(path)), EditableFont.Open(font));
                    Fonts.Add(last);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AgsFormatException or ArgumentException)
                {
                    errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
            if (last != null)
                SelectedFont = last;
            if (errors.Count > 0)
                StatusText = Loc.T("Could not open: ") + string.Join("; ", errors);
        }

        /// <summary>Called by the view with the pixel picked by Ctrl+click.</summary>
        public void PickColor(PixelPoint p)
        {
            if (SelectedItem is not { } item || _sprites == null)
                return;
            SpriteImage image = ShowOriginal ? _sprites.Read(item.Number) : Current(item.Number);
            if (p.X >= image.Width || p.Y >= image.Height)
                return;
            uint c = image[p.X, p.Y];
            ColorText = c >> 24 == 255 ? $"#{c & 0xFFFFFF:X6}" : $"#{c:X8}";
            StatusText = Loc.F("Color {0} picked.", ColorText);
        }

        [RelayCommand]
        public void Undo()
        {
            if (SelectedItem is not { } item || !_undo.TryGetValue(item.Number, out var stack) || stack.Count == 0)
            {
                StatusText = Loc.T("Nothing to undo.");
                return;
            }
            CommitEdit(item.Number, stack.Pop());
            StatusText = Loc.T("Undone.");
        }

        /// <summary>Drops all changes of the selected sprite (can be undone).</summary>
        [RelayCommand]
        public void Revert()
        {
            if (SelectedItem is not { } item || !_edited.ContainsKey(item.Number) || BeginEdit() == null)
                return;
            CommitEdit(item.Number, _sprites!.Read(item.Number));
            StatusText = Loc.F("Sprite {0} is the game's original again.", item.Number);
        }

        internal static uint? ParseColor(string? text)
        {
            string t = (text ?? "").Trim().TrimStart('#');
            if (!uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint v))
                return null;
            return t.Length switch { 6 => 0xFF000000u | v, 8 => v, _ => null };
        }

        // ── Files ──

        /// <summary>Replaces the selected sprite with a PNG file.</summary>
        public void ImportPng(string path)
        {
            if (SelectedItem is not { } item || _sprites?.File[item.Number] is not { } e)
                return;
            try
            {
                SpriteImage image = Png.Read(path);
                if (BeginEdit() == null)
                    return;
                CommitEdit(item.Number, image);
                StatusText = image.Width != e.Width || image.Height != e.Height
                    ? Loc.F("Sprite {0} replaced. Note: the size changes from {1} × {2} to {3} × {4}; the game may place or cut it differently.", item.Number, e.Width, e.Height, image.Width, image.Height)
                    : Loc.F("Sprite {0} replaced with {1}.", item.Number, Path.GetFileName(path));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                StatusText = Loc.F("Could not read {0}: {1}", Path.GetFileName(path), ex.Message);
            }
        }

        /// <summary>Takes all PNG files named after sprite numbers that differ from the game's sprites.</summary>
        public async Task ImportFolderAsync(string folder)
        {
            if (_sprites == null)
                return;
            IsBusy = true;
            StatusText = Loc.T("Reading PNG files...");
            try
            {
                var notes = new List<string>();
                Dictionary<int, SpriteImage> changed = await Task.Run(() => _sprites.LoadChanged(folder, notes));
                foreach (var (number, image) in changed)
                {
                    _undo.Remove(number);
                    _edited[number] = image;
                    if (_all.FirstOrDefault(i => i.Number == number) is { } item)
                    {
                        item.IsChanged = true;
                        item.Refresh(image);
                    }
                }
                if (changed.Count > 0)
                    PatchIsCurrent = false;
                OnPropertyChanged(nameof(HasChanges));
                ApplyFilter();
                ShowImage();
                StatusText = Loc.F("{0} changed sprites taken from the folder.", changed.Count)
                    + (notes.Count > 0 ? " " + string.Join(" ", notes.Take(3)) + (notes.Count > 3 ? " ..." : "") : "");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText = Loc.F("Could not read the folder: {0}", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Saves sprites (as they are now, edited or not) as PNG files.</summary>
        /// <param name="fileName">File name for a single sprite; otherwise <see cref="GameSprites.PngName"/>.</param>
        public async Task ExportAsync(IReadOnlyList<int> numbers, string folder, string? fileName = null)
        {
            IsBusy = true;
            try
            {
                int done = await Task.Run(() =>
                {
                    Directory.CreateDirectory(folder);
                    int n = 0;
                    foreach (int number in numbers)
                    {
                        Png.Write(Path.Combine(folder, fileName != null && numbers.Count == 1 ? fileName : GameSprites.PngName(number)), Current(number));
                        n++;
                    }
                    return n;
                });
                StatusText = Loc.F("{0} sprites saved as PNG in {1}.", done, folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AgsFormatException)
            {
                StatusText = Loc.F("Export failed: {0}", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public IReadOnlyList<int> VisibleNumbers => Items.Select(i => i.Number).ToList();
        public IReadOnlyList<int> ChangedNumbers => _edited.Keys.Order().ToList();

        /// <summary>
        /// Writes acsprset.spr and sprindex.dat with all changed sprites into <paramref name="folder"/>, and the
        /// changed sprites as PNG files into its "sprites" subfolder, so the work can be opened again.
        /// </summary>
        public async Task BuildPatchAsync(string folder)
        {
            if (_sprites == null || _edited.Count == 0)
            {
                StatusText = Loc.T("No changed sprites.");
                return;
            }
            IsBusy = true;
            var changed = new Dictionary<int, SpriteImage>(_edited);
            var progress = new Progress<double>(p => StatusText = Loc.F("Writing the sprite file... {0:0}%", p * 100));
            try
            {
                await Task.Run(() =>
                {
                    _sprites.WritePatch(changed, folder, progress);
                    string pngFolder = Path.Combine(folder, "sprites");
                    Directory.CreateDirectory(pngFolder);
                    foreach (var (number, image) in changed)
                        Png.Write(Path.Combine(pngFolder, GameSprites.PngName(number)), image);
                });
                PatchIsCurrent = true;
                long size = new FileInfo(Path.Combine(folder, SpriteFile.FileName)).Length;
                StatusText = Loc.F("Sprite patch with {0} changed sprites written to {1} ({2:0.0} MB). Install it together with the translation.",
                    changed.Count, folder, size / 1048576.0);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AgsFormatException or ArgumentException)
            {
                StatusText = Loc.F("Could not write the sprite patch: {0}", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
