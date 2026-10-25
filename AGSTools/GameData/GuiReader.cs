using System.Collections.Generic;

namespace AGSTools
{
    /// <summary>
    /// Reads GUI data (Common/gui/*.cpp). Only control texts are kept.
    /// </summary>
    internal sealed class GuiReader
    {
        // kGuiVersion_* (Common/gui/guidefines.h)
        private const int V214 = 100;
        private const int V222 = 101;
        private const int V230 = 102;
        private const int Unkn104 = 104;
        private const int Unkn106 = 106;
        private const int Unkn107 = 107;
        private const int Unkn108 = 108;
        private const int Unkn109 = 109;
        private const int V272a = 111;
        private const int V272b = 112;
        private const int V272c = 113;
        private const int V272d = 114;
        private const int V340 = 118;
        private const int V350 = 119;

        private const int LegacyMaxObjsOnGui = 30;
        private const int ListBoxSvgIndexFlag = 0x04;

        private readonly AgsReader _r;
        private readonly int _version;

        public GuiReader(AgsReader reader, int version)
        {
            _r = reader;
            _version = version;
        }

        private bool Legacy => _version < V350;

        public void SkipGuiMain()
        {
            if (Legacy)
                _r.Skip(4); // text window flags

            if (_version < V340)
            {
                _r.Skip(16 + 20); // name, OnClick handler
            }
            else
            {
                _r.ReadPrefixedString();
                _r.ReadPrefixedString();
            }
            _r.Skip(4 * 4); // x, y, width, height
            if (Legacy)
                _r.ReadInt32(); // focused control
            int controlCount = _r.ReadCount("GUI control", 100_000);
            _r.Skip(5 * 4); // popup style, popup y, colors, background image
            if (Legacy)
                _r.Skip(5 * 4); // runtime mouse state
            _r.Skip(5 * 4); // flags, transparency, z-order, id, padding
            if (Legacy)
            {
                _r.Skip(5 * 4); // reserved
                _r.ReadInt32(); // visibility
            }
            if (_version < V340)
                _r.Skip(LegacyMaxObjsOnGui * 4); // unused pointers
            _r.Skip(controlCount * 4L);
            if (_version < V340 && controlCount < LegacyMaxObjsOnGui)
                _r.Skip((LegacyMaxObjsOnGui - controlCount) * 4L);
        }

        public void ReadControls(List<GuiText> texts)
        {
            int buttons = _r.ReadCount("GUI button", 100_000);
            for (int i = 0; i < buttons; i++)
                ReadButton(texts);

            int labels = _r.ReadCount("GUI label", 100_000);
            for (int i = 0; i < labels; i++)
                ReadLabel(texts);

            int invWindows = _r.ReadCount("GUI inventory window", 100_000);
            for (int i = 0; i < invWindows; i++)
            {
                ReadControlBase();
                if (_version >= Unkn109)
                    _r.Skip((Legacy ? 4 : 3) * 4);
            }

            if (_version >= V214)
            {
                int sliders = _r.ReadCount("GUI slider", 100_000);
                for (int i = 0; i < sliders; i++)
                {
                    ReadControlBase();
                    _r.Skip(3 * 4); // min, max, value
                    if (Legacy)
                        _r.ReadInt32();
                    if (_version >= Unkn104)
                        _r.Skip(3 * 4);
                }
            }
            if (_version >= V222)
            {
                int textBoxes = _r.ReadCount("GUI text box", 100_000);
                for (int i = 0; i < textBoxes; i++)
                {
                    string name = ReadControlBase();
                    string text = Legacy ? _r.ReadFixedString(200) : _r.ReadPrefixedString();
                    _r.Skip(3 * 4); // font, color, flags
                    Add(texts, GuiTextKind.TextBox, name, text);
                }
            }
            if (_version >= V230)
            {
                int listBoxes = _r.ReadCount("GUI list box", 100_000);
                for (int i = 0; i < listBoxes; i++)
                    ReadListBox(texts);
            }
        }

        private void ReadButton(List<GuiText> texts)
        {
            string name = ReadControlBase();
            _r.Skip(3 * 4); // images
            if (Legacy)
                _r.Skip(3 * 4); // runtime state
            _r.Skip(6 * 4); // font, text color, click actions and data
            string text = Legacy ? _r.ReadFixedString(50) : _r.ReadPrefixedString();
            if (_version >= V272a)
                _r.Skip((Legacy ? 2 : 1) * 4); // alignment (+ reserved)
            Add(texts, GuiTextKind.Button, name, text);
        }

        private void ReadLabel(List<GuiText> texts)
        {
            string name = ReadControlBase();
            string text = _version < V272c ? _r.ReadFixedString(200) : _r.ReadPrefixedString();
            _r.Skip(3 * 4); // font, color, alignment
            Add(texts, GuiTextKind.Label, name, text);
        }

        private void ReadListBox(List<GuiText> texts)
        {
            string name = ReadControlBase();
            int itemCount = _r.ReadCount("list box item", 1_000_000);
            if (Legacy)
                _r.Skip(6 * 4); // runtime state
            _r.Skip(3 * 4); // font, colors
            int flags = _r.ReadInt32();
            if (_version >= V272b)
                _r.Skip((Legacy ? 2 : 1) * 4);
            if (_version >= Unkn107)
                _r.ReadInt32(); // selected background color
            for (int i = 0; i < itemCount; i++)
                Add(texts, GuiTextKind.ListBoxItem, name, _r.ReadCString());
            if (_version >= V272d && Legacy && (flags & ListBoxSvgIndexFlag) != 0)
                _r.Skip(itemCount * 2L);
        }

        /// <summary>Reads the common GUIObject fields and returns the control's script name.</summary>
        private string ReadControlBase()
        {
            _r.Skip(6 * 4); // flags, x, y, width, height, z-order
            if (Legacy)
                _r.ReadInt32(); // activated
            string name = _version >= Unkn106 ? _r.ReadCString() : "";
            if (_version >= Unkn108)
            {
                int handlers = _r.ReadCount("GUI event handler", 1000);
                for (int i = 0; i < handlers; i++)
                    _r.ReadCString();
            }
            return name;
        }

        private static void Add(List<GuiText> texts, GuiTextKind kind, string name, string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                texts.Add(new GuiText(kind, name, text));
        }
    }
}
