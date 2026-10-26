using AGSFontEditor.Localization;
using System;
using System.Collections.Generic;
using AGSFontEditor.Fonts;
using AGSTools;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AGSFontEditor.ViewModels
{
    /// <summary>A font in the font list.</summary>
    public partial class FontItemViewModel : ObservableObject, IDisposable
    {
        private EditableFont? _editable;

        public FontItemViewModel(GameFont font) => Font = font;

        public GameFont Font { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Title))]
        private bool _isModified;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ErrorText), nameof(HasError))]
        private string? _openError;

        public string Title => $"{Loc.F("Font {0}", Font.Number)}   {Font.FileName}{(IsModified ? "  •" : "")}";

        public string Details
        {
            get
            {
                var parts = new List<string>
                {
                    Font.Kind == FontKind.Wfn ? "WFN" : "TTF",
                    Font.Origin == FontOrigin.Package ? Loc.T("in package") : Loc.T("loose file"),
                };
                if (Font.Ttf is { Name.Length: > 0 } ttf)
                    parts.Add(ttf.Name);
                if (Font.Info is { } info)
                {
                    parts.Add(info.SizeIsMultiplier ? Loc.F("size ×{0}", info.Size) : Loc.F("size {0}", info.Size));
                    parts.Add(info.Outline switch
                    {
                        FontInfo.NoOutline => Loc.T("no outline"),
                        FontInfo.AutoOutline => Loc.T("auto outline"),
                        int n => Loc.F("outline font {0}", n),
                    });
                }
                if (Font.Wfn != null)
                    parts.Add(Loc.F("{0} glyphs", Font.Wfn.Glyphs.FindAll(g => g is { IsEmpty: false }).Count));
                else if (Font.Ttf != null)
                    parts.Add(Loc.F("{0} chars, {1} glyphs", Font.Ttf.CodePoints.Count, Font.Ttf.GlyphCount));
                return string.Join(" · ", parts);
            }
        }

        public string? ErrorText => Font.Error ?? OpenError;

        public bool HasError => ErrorText != null;

        /// <summary>Opens the font for viewing and editing; null (with <see cref="OpenError"/> set) if that fails.</summary>
        public EditableFont? GetEditable()
        {
            if (_editable == null && ErrorText == null)
            {
                try
                {
                    _editable = EditableFont.Open(Font);
                }
                catch (AgsFormatException ex)
                {
                    OpenError = ex.Message;
                }
            }
            return _editable;
        }

        public void Dispose() => _editable?.Dispose();
    }

    /// <summary>A cell of the glyph grid.</summary>
    public partial class GlyphCellViewModel : ObservableObject
    {
        public GlyphCellViewModel(int code, string label)
        {
            Code = code;
            Label = label;
        }

        public int Code { get; }

        /// <summary>Hex code and character, e.g. "E4 ä".</summary>
        public string Label { get; }

        [ObservableProperty]
        private Avalonia.Media.Imaging.Bitmap? _image;

        [ObservableProperty]
        private bool _hasGlyph;
    }

    /// <summary>A text encoding for looking up characters, as a translation would use it.</summary>
    public sealed record EncodingChoice(string Name, int Codepage)
    {
        public TranslationOptions ToOptions() =>
            Codepage == AgsTextEncoding.Utf8Codepage
                ? new TranslationOptions { Encoding = "UTF-8" }
                : new TranslationOptions { AnsiCodepage = Codepage };

        public override string ToString() => Name;
    }
}
