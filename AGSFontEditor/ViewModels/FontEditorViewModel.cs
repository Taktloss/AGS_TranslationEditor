using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AGSFontEditor.Fonts;
using AGSFontEditor.Rendering;
using AGSTools;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AGSFontEditor.ViewModels
{
    /// <summary>
    /// Font unpacker, viewer and editor: the fonts of a game (or loose font files), the glyph grid of the
    /// selected font, a pixel editor for one glyph and a preview of a sample text.
    /// </summary>
    public partial class FontEditorViewModel : ObservableObject
    {
        public const string DefaultSample = "Grüße, „Dämon“! Äpfel, Öl, Übel – “quoted” ‚single‘ «guillemets» …\nThe quick brown fox jumps over the lazy dog. 0123456789";

        private List<GlyphCellViewModel> _allCells = new();
        private readonly Stack<WfnGlyph?> _undo = new(), _redo = new();
        private WfnGlyph? _strokeSnapshot;
        private bool _loadingEditor;
        private int _cellScale = 1;

        public FontEditorViewModel()
        {
            Encodings = new ObservableCollection<EncodingChoice>(new[]
            {
                new EncodingChoice("UTF-8 (Unicode code points)", AgsTextEncoding.Utf8Codepage),
                new EncodingChoice("8-bit Windows-1252 (Western)", 1252),
                new EncodingChoice("8-bit Windows-1250 (Central European)", 1250),
                new EncodingChoice("8-bit Windows-1251 (Cyrillic)", 1251),
                new EncodingChoice("8-bit Windows-1253 (Greek)", 1253),
                new EncodingChoice("8-bit Windows-1254 (Turkish)", 1254),
                new EncodingChoice("8-bit Windows-1257 (Baltic)", 1257),
            });
            _selectedEncoding = Encodings[0];
        }

        public ObservableCollection<FontItemViewModel> Fonts { get; } = new();

        public ObservableCollection<GlyphCellViewModel> Cells { get; } = new();

        public ObservableCollection<EncodingChoice> Encodings { get; }

        public string[] Filters { get; } = { "All codes", "With glyph", "Missing" };

        /// <summary>The game the fonts were loaded from, or null for loose font files.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WindowTitle))]
        private string? _gamePath;

        public string WindowTitle => GamePath != null ? $"AGS Font Editor – {Path.GetFileName(GamePath)}" : "AGS Font Editor";

        [ObservableProperty]
        private string _statusText = "Open a game or a font file.";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasFont), nameof(IsTtf))]
        private FontItemViewModel? _selectedFont;

        public bool HasFont => SelectedFont?.GetEditable() != null;

        public bool IsTtf => SelectedFont?.GetEditable() is TtfEditableFont;

        public bool HasUnsavedChanges => Fonts.Any(f => f.IsModified);

        [ObservableProperty]
        private int _filterIndex;

        [ObservableProperty]
        private string _goToText = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanCreate))]
        private GlyphCellViewModel? _selectedCell;

        // ── Glyph editor ──

        [ObservableProperty]
        private WfnGlyph? _editGlyph;

        [ObservableProperty]
        private string _editTitle = "";

        [ObservableProperty]
        private string _editHint = "";

        [ObservableProperty]
        private string _editMessage = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanCreate))]
        private bool _canEdit;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanCreate))]
        private bool _hasEditGlyph;

        public bool CanCreate => CanEdit && !HasEditGlyph && SelectedCell != null;

        [ObservableProperty]
        private bool _heightEditable;

        [ObservableProperty]
        private decimal? _editWidth;

        [ObservableProperty]
        private decimal? _editHeight;

        [ObservableProperty]
        private int? _baselineRow;

        [ObservableProperty]
        private int _zoom = 16;

        [ObservableProperty]
        private string _copyFromText = "";

        [ObservableProperty]
        private decimal? _gridSize;

        [ObservableProperty]
        private string _gridInfo = "";

        [ObservableProperty]
        private bool _canUndo;

        [ObservableProperty]
        private bool _canRedo;

        // ── Preview ──

        [ObservableProperty]
        private string _previewText = DefaultSample;

        [ObservableProperty]
        private EncodingChoice _selectedEncoding;

        [ObservableProperty]
        private int _previewZoom = 3;

        [ObservableProperty]
        private Bitmap? _previewImage;

        [ObservableProperty]
        private string _missingText = "";

        private EditableFont? Editable => SelectedFont?.GetEditable();

        // ── Loading and exporting ──

        /// <summary>Loads all fonts of a game (exe, .ags or game28.dta).</summary>
        public async Task LoadGameAsync(string gamePath)
        {
            IsBusy = true;
            StatusText = $"Loading fonts of {Path.GetFileName(gamePath)}...";
            try
            {
                var (fonts, info) = await Task.Run(() =>
                {
                    GameInfo? gameInfo = null;
                    try { gameInfo = GameInfo.GetGameInfo(gamePath); }
                    catch (Exception ex) when (ex is IOException or AgsFormatException) { }
                    return (GameFonts.Load(gamePath), gameInfo);
                });
                SetFonts(fonts);
                GamePath = gamePath;
                if (info != null && !info.IsUnicode && info.TextCodepage > 0 && Encodings.All(e => e.Codepage != info.TextCodepage))
                    Encodings.Add(new EncodingChoice($"8-bit Windows-{info.TextCodepage}", info.TextCodepage));
                string encoding = info == null ? "" : info.IsUnicode ? ", game text is UTF-8" : $", game text codepage {(info.TextCodepage > 0 ? info.TextCodepage : 1252)}";
                StatusText = fonts.Count == 0
                    ? $"No fonts found in {Path.GetFileName(gamePath)}."
                    : $"{fonts.Count} fonts in {Path.GetFileName(gamePath)}{encoding}.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AgsFormatException)
            {
                StatusText = $"Could not load the game: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Opens loose font files (.ttf/.wfn) instead of a game.</summary>
        public void OpenFontFiles(IEnumerable<string> paths)
        {
            var fonts = new List<GameFont>();
            var errors = new List<string>();
            foreach (string path in paths)
            {
                try
                {
                    fonts.Add(GameFont.FromFile(path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
            if (fonts.Count == 0)
            {
                StatusText = errors.Count > 0 ? "Could not open: " + string.Join("; ", errors) : "No font opened.";
                return;
            }
            SetFonts(fonts);
            GamePath = null;
            StatusText = $"{fonts.Count} font file(s) opened." + (errors.Count > 0 ? " Errors: " + string.Join("; ", errors) : "");
        }

        private void SetFonts(List<GameFont> fonts)
        {
            SelectedFont = null;
            foreach (var f in Fonts)
                f.Dispose();
            Fonts.Clear();
            foreach (GameFont font in fonts)
                Fonts.Add(new FontItemViewModel(font));
            SelectedFont = Fonts.FirstOrDefault();
        }

        /// <summary>Files that <see cref="Export"/> would overwrite.</summary>
        public static List<string> ExistingExportFiles(IEnumerable<FontItemViewModel> fonts, string folder) =>
            fonts.Select(f => Path.Combine(folder, f.Font.FileName)).Where(File.Exists).ToList();

        /// <summary>True if <paramref name="folder"/> is the folder of the loaded game.</summary>
        public bool IsGameFolder(string folder) =>
            GamePath != null && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)),
                Path.GetDirectoryName(Path.GetFullPath(GamePath)), StringComparison.OrdinalIgnoreCase);

        /// <summary>Writes the original font files (without edits) into a folder.</summary>
        public void Export(IReadOnlyList<FontItemViewModel> fonts, string folder)
        {
            try
            {
                var written = GameFonts.Export(fonts.Select(f => f.Font), folder, overwrite: true);
                StatusText = $"Exported {written.Count} font file(s) to {folder}.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText = $"Export failed: {ex.Message}";
            }
        }

        /// <summary>Saves the selected font with its changes to <paramref name="path"/> (chosen by the user).</summary>
        public bool SaveSelected(string path)
        {
            if (SelectedFont == null || Editable == null)
                return false;
            try
            {
                byte[] data = Editable.ToArray();
                string temp = path + ".tmp";
                File.WriteAllBytes(temp, data);
                File.Move(temp, path, overwrite: true);
                Editable.MarkSaved();
                SelectedFont.IsModified = false;
                StatusText = $"Saved {Path.GetFileName(path)} ({data.Length} bytes).";
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AgsFormatException)
            {
                StatusText = $"Save failed: {ex.Message}";
                return false;
            }
        }

        // ── Font selection and glyph grid ──

        partial void OnSelectedFontChanged(FontItemViewModel? value)
        {
            _allCells = new List<GlyphCellViewModel>();
            Cells.Clear();
            SelectedCell = null;
            LoadEditor();
            EditableFont? font = value?.GetEditable();
            if (font != null)
            {
                _cellScale = Math.Clamp(48 / Math.Max(1, font.LineHeight), 1, 6);
                foreach (int code in font.GridCodes)
                {
                    var cell = new GlyphCellViewModel(code, CellLabel(code));
                    UpdateCell(font, cell);
                    _allCells.Add(cell);
                }
                if (font is TtfEditableFont ttf)
                {
                    GridSize = ttf.Grid?.Size ?? Math.Max(1, ttf.UnitsPerEm / ttf.PixelSize);
                    GridInfo = ttf.Grid != null
                        ? $"Pixel grid: {ttf.Grid.Size} units per pixel ({ttf.UnitsPerEm / (double)ttf.Grid.Size:0.##} px per em), game size {ttf.PixelSize} px"
                        : $"No pixel grid detected; game size {ttf.PixelSize} px";
                }
                else
                    GridInfo = "";
            }
            else if (value != null)
                StatusText = $"{value.Font.FileName}: {value.ErrorText}";
            ApplyFilter();
            SelectedCell = Cells.FirstOrDefault(c => c.Code == 'A') ?? Cells.FirstOrDefault();
            RenderPreview();
        }

        private static string CellLabel(int code)
        {
            string ch = code is >= 0x20 and not (>= 0x7F and < 0xA0) && code <= 0x10FFFF && !(code is >= 0xD800 and <= 0xDFFF)
                ? char.ConvertFromUtf32(code) : "";
            return code <= 0xFF ? $"{code:X2} {ch}" : $"{code:X4} {ch}";
        }

        private void UpdateCell(EditableFont font, GlyphCellViewModel cell)
        {
            GlyphPixels? glyph = font.Render(cell.Code);
            cell.HasGlyph = glyph != null;
            if (glyph == null)
            {
                cell.Image = null;
                return;
            }
            var canvas = new PixelCanvas(Math.Max(glyph.Advance, glyph.Bitmap.Width), Math.Max(font.LineHeight, glyph.Top + glyph.Bitmap.Height), TextPreview.Background);
            canvas.DrawGlyph(glyph.Bitmap, 0, glyph.Top, TextPreview.Ink);
            cell.Image = canvas.ToBitmap(_cellScale);
        }

        partial void OnFilterIndexChanged(int value) => ApplyFilter();

        private void ApplyFilter()
        {
            GlyphCellViewModel? selected = SelectedCell;
            Cells.Clear();
            foreach (var cell in _allCells.Where(c => FilterIndex switch { 1 => c.HasGlyph, 2 => !c.HasGlyph, _ => true }))
                Cells.Add(cell);
            if (selected != null && Cells.Contains(selected))
                SelectedCell = selected;
        }

        /// <summary>
        /// Parses a character code: a single character (looked up as the engine would with the preview
        /// encoding), "U+201E", "0x84" or a decimal number.
        /// </summary>
        public int ParseCode(string text)
        {
            text = text.Trim();
            if (text.Length == 0)
                return -1;
            if (text.EnumerateRunes().Count() == 1 && !(text.Length == 1 && char.IsDigit(text[0])))
                return FontCoverage.EngineCode(text, SelectedEncoding.ToOptions());
            string hex = text.StartsWith("U+", StringComparison.OrdinalIgnoreCase) || text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : "";
            if (hex.Length > 0)
                return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int h) && h <= 0x10FFFF ? h : -1;
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int d) && d <= 0x10FFFF ? d : -1;
        }

        [RelayCommand]
        private void GoTo()
        {
            int code = ParseCode(GoToText);
            if (code < 0 || Editable == null)
            {
                StatusText = $"'{GoToText}' is not a character or code (examples: ä, U+201E, 0x84, 228).";
                return;
            }
            GlyphCellViewModel? cell = _allCells.FirstOrDefault(c => c.Code == code);
            if (cell == null)
            {
                // A code outside the grid, e.g. a new character for a TrueType font
                cell = new GlyphCellViewModel(code, CellLabel(code));
                UpdateCell(Editable, cell);
                int index = _allCells.FindIndex(c => c.Code > code);
                _allCells.Insert(index < 0 ? _allCells.Count : index, cell);
            }
            if (!Cells.Contains(cell))
            {
                FilterIndex = 0;
                ApplyFilter();
            }
            SelectedCell = cell;
        }

        partial void OnSelectedCellChanged(GlyphCellViewModel? value)
        {
            _undo.Clear();
            _redo.Clear();
            LoadEditor();
        }

        // ── Glyph editor ──

        private void LoadEditor()
        {
            _loadingEditor = true;
            try
            {
                EditableFont? font = Editable;
                GlyphCellViewModel? cell = SelectedCell;
                CanEdit = font != null && cell != null && font.EditBlocker == null;
                EditGlyph = font != null && cell != null && CanEdit ? font.GetEditGlyph(cell.Code) : null;
                HasEditGlyph = EditGlyph != null;
                EditWidth = EditGlyph?.Width;
                EditHeight = EditGlyph?.Height;
                BaselineRow = font?.BaselineRow;
                HeightEditable = font is { FixedHeight: false };
                EditTitle = cell == null ? "" : $"U+{cell.Code:X4}  {CellLabel(cell.Code)[(cell.Code <= 0xFF ? 3 : 5)..]}   (code {cell.Code})";
                EditHint = cell == null ? "" : CodeHint(cell.Code);
                EditMessage = font == null || cell == null ? ""
                    : font.EditBlocker ?? (EditGlyph == null ? "This code has no glyph. Create an empty one or copy another character's glyph." : "");
                CanUndo = _undo.Count > 0;
                CanRedo = _redo.Count > 0;
            }
            finally
            {
                _loadingEditor = false;
            }
        }

        /// <summary>Explains 8-bit codes: which character of the preview codepage they stand for.</summary>
        private string CodeHint(int code)
        {
            if (code is < 0x80 or > 0xFF || SelectedEncoding.Codepage == AgsTextEncoding.Utf8Codepage)
                return code is >= 0x80 and < 0xA0
                    ? "Control code in Unicode; in an 8-bit translation it can stand for a character like „ (0x84 in 1252)."
                    : "";
            string ch = AgsTextEncoding.ForCodepage(SelectedEncoding.Codepage).GetString(new[] { (byte)code });
            return $"In an 8-bit translation with codepage {SelectedEncoding.Codepage} this code is “{ch}”.";
        }

        /// <summary>Called by the view before a pixel stroke changes the bitmap.</summary>
        public void BeginStroke() => _strokeSnapshot = EditGlyph?.Clone();

        /// <summary>Called by the view after a pixel stroke.</summary>
        public void EndStroke()
        {
            if (EditGlyph == null || _strokeSnapshot == null || _strokeSnapshot.Equals(EditGlyph))
                return;
            PushUndo(_strokeSnapshot);
            _strokeSnapshot = null;
            Commit(EditGlyph);
        }

        private void PushUndo(WfnGlyph? state)
        {
            _undo.Push(state?.Clone());
            _redo.Clear();
            CanUndo = true;
            CanRedo = false;
        }

        /// <summary>Stores the glyph in the font and refreshes the grid and preview.</summary>
        private void Commit(WfnGlyph? glyph)
        {
            EditableFont? font = Editable;
            GlyphCellViewModel? cell = SelectedCell;
            if (font == null || cell == null || SelectedFont == null)
                return;
            try
            {
                if (glyph == null)
                    font.DeleteGlyph(cell.Code);
                else
                    font.SetGlyph(cell.Code, glyph);
            }
            catch (AgsFormatException ex)
            {
                StatusText = $"Could not change the glyph: {ex.Message}";
                return;
            }
            SelectedFont.IsModified = true;
            UpdateCell(font, cell);
            RenderPreview();
        }

        /// <summary>Replaces the edited glyph (with undo) and stores it.</summary>
        private void ReplaceGlyph(WfnGlyph? glyph)
        {
            PushUndo(EditGlyph);
            Commit(glyph);
            _loadingEditor = true;
            EditGlyph = glyph?.Clone();
            HasEditGlyph = glyph != null;
            EditWidth = glyph?.Width;
            EditHeight = glyph?.Height;
            EditMessage = glyph == null ? "This code has no glyph. Create an empty one or copy another character's glyph." : "";
            _loadingEditor = false;
        }

        partial void OnEditWidthChanged(decimal? value) => ResizeFromInput();
        partial void OnEditHeightChanged(decimal? value) => ResizeFromInput();

        private void ResizeFromInput()
        {
            if (_loadingEditor || EditGlyph == null || EditWidth is not decimal w || EditHeight is not decimal h)
                return;
            int width = (int)Math.Clamp(w, 0, 255), height = (int)Math.Clamp(h, 1, 255);
            if (width == EditGlyph.Width && height == EditGlyph.Height)
                return;
            ReplaceGlyph(EditGlyph.Resize(width, height));
        }

        [RelayCommand]
        private void CreateGlyph()
        {
            if (Editable == null || !CanEdit)
                return;
            ReplaceGlyph(Editable.CreateEmptyGlyph());
        }

        [RelayCommand]
        private void CopyFrom()
        {
            EditableFont? font = Editable;
            if (font == null || !CanEdit || SelectedCell == null)
                return;
            int code = ParseCode(CopyFromText);
            WfnGlyph? source = code >= 0 ? font.GetEditGlyph(code) : null;
            if (source == null)
            {
                StatusText = $"'{CopyFromText}' has no glyph in this font.";
                return;
            }
            ReplaceGlyph(source);
            StatusText = $"Copied the glyph of {CellLabel(code)} (code {code}).";
        }

        [RelayCommand]
        private void DeleteGlyph()
        {
            if (Editable == null || !CanEdit || EditGlyph == null)
                return;
            ReplaceGlyph(null);
        }

        [RelayCommand]
        private void ClearGlyph()
        {
            if (EditGlyph == null || !CanEdit)
                return;
            ReplaceGlyph(new WfnGlyph(EditGlyph.Width, EditGlyph.Height));
        }

        [RelayCommand]
        private void Undo() => StepHistory(_undo, _redo);

        [RelayCommand]
        private void Redo() => StepHistory(_redo, _undo);

        private void StepHistory(Stack<WfnGlyph?> from, Stack<WfnGlyph?> to)
        {
            if (from.Count == 0 || !CanEdit)
                return;
            to.Push(EditGlyph?.Clone());
            WfnGlyph? state = from.Pop();
            Commit(state);
            _loadingEditor = true;
            EditGlyph = state?.Clone();
            HasEditGlyph = state != null;
            EditWidth = state?.Width;
            EditHeight = state?.Height;
            _loadingEditor = false;
            CanUndo = _undo.Count > 0;
            CanRedo = _redo.Count > 0;
        }

        [RelayCommand]
        private void ApplyGridSize()
        {
            if (Editable is not TtfEditableFont ttf || GridSize is not decimal size || size < 1)
                return;
            ttf.SetGridSize((int)size);
            GridInfo = $"Pixel grid set to {(int)size} units per pixel ({ttf.UnitsPerEm / size:0.##} px per em), game size {ttf.PixelSize} px";
            _undo.Clear();
            _redo.Clear();
            LoadEditor();
        }

        // ── Preview ──

        partial void OnPreviewTextChanged(string value) => RenderPreview();
        partial void OnPreviewZoomChanged(int value) => RenderPreview();

        partial void OnSelectedEncodingChanged(EncodingChoice value)
        {
            RenderPreview();
            if (SelectedCell != null)
                EditHint = CodeHint(SelectedCell.Code);
        }

        private void RenderPreview()
        {
            EditableFont? font = Editable;
            if (font == null)
            {
                PreviewImage = null;
                MissingText = "";
                return;
            }
            var preview = TextPreview.Render(font, PreviewText ?? "", SelectedEncoding.ToOptions());
            PreviewImage = preview.Canvas.ToBitmap(Math.Clamp(PreviewZoom, 1, 8));
            MissingText = preview.Missing.Count == 0
                ? "All characters of the sample have a glyph."
                : "Missing: " + string.Join("  ", preview.Missing.Select(DescribeMissing));
        }

        private string DescribeMissing(string character)
        {
            int code = FontCoverage.EngineCode(character, SelectedEncoding.ToOptions());
            return code < 0 ? $"{character} (not in codepage)" : code <= 0xFF ? $"{character} ({code:X2})" : $"{character} (U+{code:X4})";
        }
    }
}
