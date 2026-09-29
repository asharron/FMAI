using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using BizHawk.Client.Common;
using BizHawk.Emulation.Common;
using ContentAlignment = System.Drawing.ContentAlignment;

namespace FMAI;

[ExternalTool("Hex Viewer")]
[ExternalToolApplicability.SingleSystem(VSystemID.Raw.SNES)]
public class HexViewer : Form, IExternalToolForm {
    public bool IsActive { get; private set; }
    public bool IsLoaded { get; private set; }

    [RequiredApi] public ApiContainer? ApiContainer { get; set; }
    [RequiredApi] public IEmulationApi? EmulationApi { get; set; }

    public void UpdateValues(ToolFormUpdateType type) => RefreshFormControls();
    public void Restart() {
        _contextKey = null;          // force reload of per-game/per-domain data
        _modifiedAddresses.Clear();
        RefreshFormControls();
    }
    public bool AskSaveChanges() => true;

    // ---------------------------------------------------------------- constants

    private const int BytesPerRow = 16;
    private const int RowHeight = 22;
    private const int PageRows = 64;                       // rows per cached page (64 * 16 = 1 KB)
    private const long DefaultMemorySize = 0x20000;
    private static readonly string[] HexStrings = Enumerable.Range(0, 256).Select(b => b.ToString("X2")).ToArray();

    // ---------------------------------------------------------------- state

    private readonly HashSet<long> _modifiedAddresses = new();
    private readonly Dictionary<long, string> _notes = new();
    private readonly Dictionary<long, string> _labels = new();
    private readonly Dictionary<long, string> _colors = new();

    // Virtual-mode byte cache: page index -> bytes. Cleared on every refresh so the
    // next paint re-reads only the pages that are actually on screen.
    private readonly Dictionary<int, byte[]> _pages = new();

    private readonly DataGridView _dataGridView;
    private readonly TextBox _noteTextBox;
    private readonly Label _selectedAddressLabel;
    private readonly Label _statusLabel;

    private long _totalBytes;
    private long _currentSelectedAddress = -1;
    private string? _contextKey;       // "rom|domain"
    private string _dataPrefix = "";   // file path prefix for the current context

    // ---------------------------------------------------------------- ctor

    public HexViewer() {
        ClientSize = new Size(920, 380);
        KeyPreview = true;
        KeyDown += (_, e) => {
            if (TryHandleShortcut(e.KeyData)) {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        MinimumSize = new Size(700, 260);
        BackColor = Theme.Back;
        ForeColor = Theme.Fore;
        SuspendLayout();

        _dataGridView = BuildGrid();

        // ---- side panel
        var rightPanel = new Panel {
            Dock = DockStyle.Right,
            Width = 240,
            BackColor = Theme.Panel,
            Padding = new Padding(10, 8, 10, 10)
        };

        _selectedAddressLabel = new Label {
            Text = "Note (No Selection):",
            Font = Theme.UiBold,
            ForeColor = Color.FromArgb(156, 220, 254),
            Dock = DockStyle.Top,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var bottomPanel = new Panel {
            Dock = DockStyle.Bottom,
            Height = 58,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 0)
        };

        var saveButton = new Button {
            Text = "Save Note",
            Font = Theme.UiBold,
            BackColor = Theme.Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Top,
            Height = 28,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.Click += (_, _) => SaveCurrentNote();

        _statusLabel = new Label {
            Font = Theme.UiSmall,
            ForeColor = Color.FromArgb(100, 200, 115),
            Dock = DockStyle.Bottom,
            Height = 20,
            TextAlign = ContentAlignment.MiddleLeft
        };

        bottomPanel.Controls.Add(saveButton);
        bottomPanel.Controls.Add(_statusLabel);

        _noteTextBox = new TextBox {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = Theme.UiInput,
            BackColor = Theme.Back,
            ForeColor = Theme.Fore,
            BorderStyle = BorderStyle.FixedSingle
        };

        rightPanel.Controls.Add(_noteTextBox);
        rightPanel.Controls.Add(_selectedAddressLabel);
        rightPanel.Controls.Add(bottomPanel);

        Controls.Add(_dataGridView);
        Controls.Add(rightPanel);

        BuildContextMenu();

        ResumeLayout(performLayout: false);
        PerformLayout();

        Load += (_, _) => {
            IsLoaded = true;
            UpdateSelectedCellNote(force: true);
        };
        Activated += (_, _) => IsActive = true;
        Deactivate += (_, _) => IsActive = false;
        FormClosed += (_, _) => IsLoaded = false;
        Shown += (_, _) => {
            ActiveControl = _dataGridView;
            RefreshFormControls();
            UpdateSelectedCellNote(force: true);
        };    
    }

    private DataGridView BuildGrid() {
        var grid = new DataGridView {
            Dock = DockStyle.Fill,
            VirtualMode = true,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeColumns = false,
            AllowUserToResizeRows = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
            BackgroundColor = Theme.Back,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.Single,
            GridColor = Color.FromArgb(50, 50, 50),
            EnableHeadersVisualStyles = false,
            RowHeadersWidth = 70,
            RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 26,
            RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            Font = Theme.Mono,
            DefaultCellStyle = MakeCellStyle(Theme.Back),
            AlternatingRowsDefaultCellStyle = MakeCellStyle(Color.FromArgb(37, 37, 38)),
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                Font = Theme.MonoBold,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.FromArgb(200, 200, 200),
                SelectionBackColor = Color.FromArgb(45, 45, 48),
                SelectionForeColor = Color.FromArgb(200, 200, 200)
            },
            RowHeadersDefaultCellStyle = new DataGridViewCellStyle {
                Font = Theme.MonoBold,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.FromArgb(140, 170, 200),
                SelectionBackColor = Color.FromArgb(45, 45, 48),
                SelectionForeColor = Color.FromArgb(140, 170, 200)
            }
        };

        // Explicit row height (before RowCount is set) so nothing depends on font/DPI guesses.
        grid.RowTemplate.Height = RowHeight;

        typeof(DataGridView)
            .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(grid, true, null);

        // ---- columns: 16 hex columns + Label
        for (int i = 0; i < BytesPerRow; i++) {
            grid.Columns.Add(new DataGridViewTextBoxColumn {
                Name = i.ToString("X"),
                HeaderText = i.ToString("X"),
                Width = 30,
                MinimumWidth = 30,
                Resizable = DataGridViewTriState.False,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        var labelStyle = new DataGridViewCellStyle {
            Font = Theme.Ui,
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 6, 0)
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn {
            Name = "Label",
            HeaderText = "Label",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 120,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = labelStyle,
            HeaderCell = { Style = { Alignment = DataGridViewContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) } }
        });

        _totalBytes = DefaultMemorySize;
        grid.RowCount = (int)(_totalBytes / BytesPerRow);

        // ---- events
        grid.CellValueNeeded += OnCellValueNeeded;
        grid.CellFormatting += OnCellFormatting;
        grid.RowPostPaint += OnRowPostPaint;
        grid.SelectionChanged += (_, _) => UpdateSelectedCellNote();
        grid.CurrentCellChanged += (_, _) => UpdateSelectedCellNote();

        return grid;
    }

    private static DataGridViewCellStyle MakeCellStyle(Color back) => new() {
        Font = Theme.Mono,
        Alignment = DataGridViewContentAlignment.MiddleCenter,
        BackColor = back,
        ForeColor = Theme.Fore,
        SelectionBackColor = Color.FromArgb(38, 79, 120),
        SelectionForeColor = Color.White,
        Padding = new Padding(2, 0, 2, 0)
    };

    private void BuildContextMenu() {
        var menu = new ContextMenuStrip {
            BackColor = Color.FromArgb(45, 45, 48),
            ForeColor = Theme.Fore,
            ShowImageMargin = false
        };

        menu.Items.Add(new ToolStripMenuItem("Jump to Address...", null, (_, _) => OpenJumpToAddressDialog()) {
            ShortcutKeyDisplayString = "Ctrl+J"
        });
        menu.Items.Add(new ToolStripMenuItem("Address Label...", null, (_, _) => OpenAddressLabelForCurrentRow()) {
            ShortcutKeyDisplayString = "L"
        });
        menu.Items.Add(new ToolStripMenuItem("Select Color...", null, (_, _) => OpenColorDialogForSelectedCells()) {
            ShortcutKeyDisplayString = "C"
        });

        foreach (ToolStripItem item in menu.Items) {
            item.BackColor = Color.FromArgb(45, 45, 48);
            item.ForeColor = Theme.Fore;
        }

        _dataGridView.ContextMenuStrip = menu;
        ContextMenuStrip = menu;
    }

    // ---------------------------------------------------------------- virtual mode

    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e) {
        if (e.RowIndex < 0) return;

        long rowAddress = (long)e.RowIndex * BytesPerRow;
        if (e.ColumnIndex < BytesPerRow) {
            byte? value = ReadCached(rowAddress + e.ColumnIndex);
            e.Value = value.HasValue ? HexStrings[value.Value] : "--";
        } else {
            e.Value = _labels.TryGetValue(rowAddress, out string? label) ? label : "";
        }
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e) {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.ColumnIndex >= BytesPerRow) return;

        long address = (long)e.RowIndex * BytesPerRow + e.ColumnIndex;
        if (_colors.TryGetValue(address, out string? colorName)
            && TryGetColorStyle(colorName, out Color back, out Color fore)) {
            e.CellStyle!.BackColor = back;
            e.CellStyle.ForeColor = fore;
        } else if (_modifiedAddresses.Contains(address)) {
            e.CellStyle!.BackColor = Color.FromArgb(80, 70, 20);
            e.CellStyle.ForeColor = Color.FromArgb(255, 235, 140);
        }
    }

    private void OnRowPostPaint(object? sender, DataGridViewRowPostPaintEventArgs e) {
        string text = ((long)e.RowIndex * BytesPerRow).ToString("X6");
        var bounds = new Rectangle(
            e.RowBounds.Left, e.RowBounds.Top,
            _dataGridView.RowHeadersWidth - 6, e.RowBounds.Height);

        TextRenderer.DrawText(
            e.Graphics, text, Theme.MonoBold, bounds,
            Color.FromArgb(140, 170, 200),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }

    /// <summary>Returns one byte from the page cache, reading the page from the emulator on a miss.</summary>
    private byte? ReadCached(long address) {
        if (address < 0 || address >= _totalBytes) return null;

        const int pageBytes = PageRows * BytesPerRow;
        int page = (int)(address / pageBytes);

        if (!_pages.TryGetValue(page, out byte[]? data)) {
            data = ReadPage(page);
            _pages[page] = data;
        }

        int index = (int)(address - (long)page * pageBytes);
        return index < data.Length ? data[index] : null;
    }

    private byte[] ReadPage(int page) {
        const int pageBytes = PageRows * BytesPerRow;
        long start = (long)page * pageBytes;
        int length = (int)Math.Min(pageBytes, _totalBytes - start);

        if (ApiContainer == null || length <= 0) return Array.Empty<byte>();

        var list = ApiContainer.Memory.ReadByteRange(start, length);
        if (list == null) return Array.Empty<byte>();

        int count = Math.Min(list.Count, length);
        var data = new byte[count];
        for (int i = 0; i < count; i++) data[i] = list[i];
        return data;
    }

    // ---------------------------------------------------------------- refresh

    private void RefreshFormControls() {
        if (ApiContainer == null) return;

        UpdateContext(ApiContainer);
        _pages.Clear();
        _dataGridView.Invalidate();
    }

    /// <summary>Detects ROM / memory-domain / size changes and reloads per-context data.</summary>
    private void UpdateContext(ApiContainer api) {
        string domain = "";
        string rom = "";
        try { domain = api.Memory.GetCurrentMemoryDomain() ?? ""; } catch { }
        try { rom = api.Emulation.GetGameInfo()?.Name ?? ""; } catch { }

        long size = api.Memory.GetCurrentMemoryDomainSize();
        if (size <= 0) size = DefaultMemorySize;

        string key = $"{rom}|{domain}";
        if (key != _contextKey) {
            _contextKey = key;
            _dataPrefix = Path.Combine(GetDataDirectory(), $"{Sanitize(rom)}__{Sanitize(domain)}");
            api.Memory.SetBigEndian(false);

            _modifiedAddresses.Clear();
            LoadAllData();
            UpdateSelectedCellNote(force: true);
        }

        if (size != _totalBytes) {
            _totalBytes = size;
            _pages.Clear();
            int rows = (int)((size + BytesPerRow - 1) / BytesPerRow);
            if (_dataGridView.RowCount != rows) {
                _dataGridView.RowCount = rows;
            }
        }
    }

    // ---------------------------------------------------------------- keyboard

    // Single place for all shortcuts. ProcessCmdKey runs before the focused control sees the key,
    // so each shortcut fires exactly once.
    private bool TryHandleShortcut(Keys keyData) {
        if (_noteTextBox.ContainsFocus) {
            if (keyData == (Keys.Control | Keys.S)) {
                SaveCurrentNote();
                return true;
            }
            return false;
        }

        if (keyData == (Keys.Control | Keys.J)) {
            OpenJumpToAddressDialog();
            return true;
        }

        if ((keyData & (Keys.Control | Keys.Alt)) == 0) {
            switch (keyData & Keys.KeyCode) {
                case Keys.Add:
                case Keys.Oemplus:
                    ModifySelectedCells(1);
                    return true;
                case Keys.Subtract:
                case Keys.OemMinus:
                    ModifySelectedCells(-1);
                    return true;
                case Keys.L:
                    OpenAddressLabelForCurrentRow();
                    return true;
                case Keys.C:
                    OpenColorDialogForSelectedCells();
                    return true;
            }
        }

        return false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        TryHandleShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);
    // ---------------------------------------------------------------- selection helpers

    private long GetCurrentSelectedAddress() {
        var cell = _dataGridView.CurrentCell;
        if (cell == null || cell.RowIndex < 0 || cell.ColumnIndex < 0) return -1;

        // Clicking the Label column maps to the first byte of that row.
        int col = cell.ColumnIndex < BytesPerRow ? cell.ColumnIndex : 0;
        return (long)cell.RowIndex * BytesPerRow + col;
    }

    private List<long> GetSelectedCellAddresses() {
        var addresses = new List<long>();

        foreach (DataGridViewCell cell in _dataGridView.SelectedCells) {
            if (cell.RowIndex >= 0 && cell.ColumnIndex >= 0 && cell.ColumnIndex < BytesPerRow) {
                addresses.Add((long)cell.RowIndex * BytesPerRow + cell.ColumnIndex);
            }
        }

        if (addresses.Count == 0) {
            var cur = _dataGridView.CurrentCell;
            if (cur != null && cur.RowIndex >= 0 && cur.ColumnIndex >= 0 && cur.ColumnIndex < BytesPerRow) {
                addresses.Add((long)cur.RowIndex * BytesPerRow + cur.ColumnIndex);
            }
        }

        return addresses.Distinct().OrderBy(a => a).ToList();
    }

    // ---------------------------------------------------------------- notes panel

    private void UpdateSelectedCellNote(bool force = false) {
        long address = GetCurrentSelectedAddress();

        if (address < 0) {
            _selectedAddressLabel.Text = "Note (No Selection):";
            _noteTextBox.Text = "";
            _statusLabel.Text = "";
            _currentSelectedAddress = -1;
            return;
        }

        if (!force && address == _currentSelectedAddress) return;

        _currentSelectedAddress = address;
        _selectedAddressLabel.Text = $"Note for Address: ${address:X6}";
        _noteTextBox.Text = _notes.TryGetValue(address, out string? note) ? note : "";
        _statusLabel.Text = "";
    }

    public void SaveCurrentNote() {
        long address = _currentSelectedAddress >= 0 ? _currentSelectedAddress : GetCurrentSelectedAddress();
        if (address < 0) return;

        string text = _noteTextBox.Text;
        if (string.IsNullOrWhiteSpace(text)) {
            _notes.Remove(address);
        } else {
            _notes[address] = text;
        }

        if (SaveMap(_dataPrefix + ".notes.json", _notes, "note")) {
            _statusLabel.Text = $"Saved note for ${address:X6}";
        }
    }

    // ---------------------------------------------------------------- dialogs / actions

    private void OpenAddressLabelForCurrentRow() {
        var cell = _dataGridView.CurrentCell;
        if (cell != null && cell.RowIndex >= 0) {
            OpenAddressLabelDialog(cell.RowIndex);
        }
    }

    public void OpenAddressLabelDialog(int rowIndex) {
        if (rowIndex < 0 || rowIndex >= _dataGridView.RowCount) return;

        long address = (long)rowIndex * BytesPerRow;
        _labels.TryGetValue(address, out string? currentLabel);

        using var dialog = new AddressLabelDialog(address, currentLabel ?? "");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string newLabel = dialog.AddressLabel;
        if (string.IsNullOrWhiteSpace(newLabel)) {
            _labels.Remove(address);
        } else {
            _labels[address] = newLabel;
        }

        SaveMap(_dataPrefix + ".labels.json", _labels, "address label");
        _dataGridView.InvalidateRow(rowIndex);
    }

    public void OpenColorDialogForSelectedCells() {
        var selected = GetSelectedCellAddresses();
        if (selected.Count == 0) return;

        _colors.TryGetValue(selected[0], out string? existing);

        using var dialog = new SelectColorDialog(selected.Count, existing ?? "");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string chosen = dialog.SelectedColor;
        bool clear = string.IsNullOrWhiteSpace(chosen) || chosen.Equals("None", StringComparison.OrdinalIgnoreCase);

        foreach (long addr in selected) {
            if (clear) _colors.Remove(addr);
            else _colors[addr] = chosen;
        }

        SaveMap(_dataPrefix + ".colors.json", _colors, "cell color");
        _dataGridView.Invalidate();
    }

    public void OpenJumpToAddressDialog() {
        long current = Math.Max(0, GetCurrentSelectedAddress());

        using var dialog = new JumpToAddressDialog(current, _totalBytes);
        if (dialog.ShowDialog(this) == DialogResult.OK) {
            JumpToAddress(dialog.TargetAddress);
        }
    }

    public void JumpToAddress(long address) {
        if (_dataGridView.RowCount == 0) return;

        int row = Math.Max(0, Math.Min((int)(address / BytesPerRow), _dataGridView.RowCount - 1));
        int col = Math.Max(0, Math.Min((int)(address % BytesPerRow), BytesPerRow - 1));
        
        _dataGridView.ClearSelection();
        _dataGridView.CurrentCell = _dataGridView.Rows[row].Cells[col];
        _dataGridView.CurrentCell.Selected = true;

        // Setting (not reading) the scroll row is fine; put the target a few rows from the top.
        try {
            _dataGridView.FirstDisplayedScrollingRowIndex = Math.Max(0, row - 3);
        } catch (InvalidOperationException) { }

        _dataGridView.Focus();
        RefreshFormControls();
    }

    // ---------------------------------------------------------------- memory editing

    private void ModifySelectedCells(int delta) {
        if (ApiContainer == null) return;

        var addresses = GetSelectedCellAddresses();
        if (addresses.Count == 0) return;

        foreach (long address in addresses) {
            if (address < 0 || address >= _totalBytes) continue;

            byte current = (byte)ApiContainer.Memory.ReadByte(address);
            byte updated = (byte)((current + delta) & 0xFF);
            ApiContainer.Memory.WriteByte(address, updated);
            _modifiedAddresses.Add(address);
        }

        _pages.Clear();
        _dataGridView.Invalidate();
    }

    // ---------------------------------------------------------------- colors

    public static bool TryGetColorStyle(string colorName, out Color backColor, out Color foreColor) {
        foreColor = Color.White;
        switch (colorName.Trim().ToLowerInvariant()) {
            case "blue":   backColor = Color.FromArgb(30, 90, 180);  return true;
            case "red":    backColor = Color.FromArgb(160, 35, 35);  return true;
            case "green":  backColor = Color.FromArgb(35, 125, 50);  return true;
            case "orange": backColor = Color.FromArgb(190, 95, 20);  return true;
            case "purple": backColor = Color.FromArgb(120, 45, 150); return true;
            default:       backColor = Color.Empty;                  return false;
        }
    }

    // ---------------------------------------------------------------- persistence

    // Data lives in %AppData%\FMAI\HexViewer and is scoped per ROM + memory domain, so notes for one
    // game (or WRAM vs. ROM) never bleed into another.
    private static string GetDataDirectory() {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FMAI", "HexViewer");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    private static string Sanitize(string name) {
        if (string.IsNullOrWhiteSpace(name)) return "unknown";
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }

    private void LoadAllData() {
        Replace(_notes, LoadMap(_dataPrefix + ".notes.json"));
        Replace(_labels, LoadMap(_dataPrefix + ".labels.json"));
        Replace(_colors, LoadMap(_dataPrefix + ".colors.json"));
        _dataGridView.Invalidate();
    }

    private static void Replace(Dictionary<long, string> target, Dictionary<long, string> source) {
        target.Clear();
        foreach (var kv in source) target[kv.Key] = kv.Value;
    }

    private static readonly Regex JsonPair =
        new("\"([^\"]+)\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Singleline);
    private static readonly Regex JsonEscape = new(@"\\(.)", RegexOptions.Singleline);

    private static string Escape(string s) => s
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n")
        .Replace("\t", "\\t");

    // Single-pass unescape: sequential Replace() calls corrupt sequences like "\\n".
    private static string Unescape(string s) => JsonEscape.Replace(s, m => m.Groups[1].Value switch {
        "n" => "\n",
        "r" => "\r",
        "t" => "\t",
        var c => c
    });

    private static Dictionary<long, string> LoadMap(string path) {
        var map = new Dictionary<long, string>();
        try {
            if (!File.Exists(path)) return map;

            foreach (Match m in JsonPair.Matches(File.ReadAllText(path))) {
                if (TryParseAddress(m.Groups[1].Value, out long address)) {
                    map[address] = Unescape(m.Groups[2].Value);
                }
            }
        } catch { }
        return map;
    }

    private bool SaveMap(string path, Dictionary<long, string> map, string what) {
        try {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            var keys = map.Keys.OrderBy(k => k).ToList();
            for (int i = 0; i < keys.Count; i++) {
                string comma = i == keys.Count - 1 ? "" : ",";
                sb.AppendLine($"  \"{keys[i]:X6}\": \"{Escape(map[keys[i]])}\"{comma}");
            }
            sb.AppendLine("}");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return true;
        } catch (Exception ex) {
            MessageBox.Show(this, $"Failed to save {what} to disk: {ex.Message}", "Save Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    public static bool TryParseAddress(string? input, out long address) {
        address = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        string text = input.Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            text = text.Substring(2).Trim();
        } else if (text.StartsWith("$") || text.StartsWith("#")) {
            text = text.Substring(1).Trim();
        }

        if (text.EndsWith("h", StringComparison.OrdinalIgnoreCase)) {
            text = text.Substring(0, text.Length - 1).Trim();
        }

        text = text.Replace(":", "").Replace(" ", "");
        if (text.Length == 0) return false;

        return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address) && address >= 0;
    }

    // ---------------------------------------------------------------- theme (shared, never disposed)

    private static class Theme {
        public static readonly Color Back = Color.FromArgb(30, 30, 30);
        public static readonly Color Panel = Color.FromArgb(37, 37, 38);
        public static readonly Color Fore = Color.FromArgb(220, 220, 220);
        public static readonly Color Accent = Color.FromArgb(14, 99, 156);
        public static readonly Color Neutral = Color.FromArgb(60, 60, 60);

        public static readonly Font Mono = new("Consolas", 9.5f, FontStyle.Regular);
        public static readonly Font MonoBold = new("Consolas", 9.5f, FontStyle.Bold);
        public static readonly Font Ui = new("Segoe UI", 9f, FontStyle.Regular);
        public static readonly Font UiBold = new("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font UiSmall = new("Segoe UI", 8.5f, FontStyle.Regular);
        public static readonly Font UiInput = new("Segoe UI", 9.5f, FontStyle.Regular);
    }

    // ---------------------------------------------------------------- dialogs

    private abstract class DarkDialog : Form {
        protected DarkDialog(string title, Size clientSize) {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = clientSize;
            Font = Theme.Ui;
            BackColor = Theme.Panel;
            ForeColor = Theme.Fore;
        }

        protected Label AddLabel(string text, bool bold) {
            var label = new Label {
                Text = text,
                Location = new Point(14, 12),
                AutoSize = true,
                Font = bold ? Theme.UiBold : Theme.Ui,
                ForeColor = Theme.Fore
            };
            Controls.Add(label);
            return label;
        }

        protected Button AddButton(string text, Point location, bool primary, DialogResult result) {
            var button = new Button {
                Text = text,
                DialogResult = result,
                Location = location,
                Size = new Size(75, 26),
                BackColor = primary ? Theme.Accent : Theme.Neutral,
                ForeColor = primary ? Color.White : Theme.Fore,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            Controls.Add(button);
            return button;
        }

        protected TextBox AddTextBox(Point location, Size size, Font font, string text) {
            var box = new TextBox {
                Location = location,
                Size = size,
                Font = font,
                BackColor = Theme.Back,
                ForeColor = Theme.Fore,
                BorderStyle = BorderStyle.FixedSingle,
                Text = text
            };
            Controls.Add(box);
            return box;
        }
    }

    private sealed class JumpToAddressDialog : DarkDialog {
        private readonly TextBox _addressTextBox;
        private readonly long _maxAddress;

        public long TargetAddress { get; private set; }

        public JumpToAddressDialog(long currentAddress, long maxAddress) : base("Jump to Address", new Size(280, 115)) {
            _maxAddress = maxAddress;

            AddLabel("Enter Address (Hex):", bold: false);
            _addressTextBox = AddTextBox(new Point(16, 34), new Size(248, 23), Theme.Mono, currentAddress.ToString("X"));

            var ok = AddButton("Jump", new Point(108, 72), primary: true, DialogResult.None);
            var cancel = AddButton("Cancel", new Point(189, 72), primary: false, DialogResult.Cancel);

            ok.Click += (_, _) => OnJump();
            AcceptButton = ok;
            CancelButton = cancel;

            Shown += (_, _) => {
                _addressTextBox.Focus();
                _addressTextBox.SelectAll();
            };
        }

        private void OnJump() {
            if (!TryParseAddress(_addressTextBox.Text, out long parsed)) {
                Warn("Please enter a valid hexadecimal address (e.g. 7E0000, $1234, 0x100).");
                return;
            }

            if (_maxAddress > 0 && parsed >= _maxAddress) {
                Warn($"Address is out of range.\nValid range: $000000 - ${_maxAddress - 1:X6}");
                return;
            }

            TargetAddress = parsed;
            DialogResult = DialogResult.OK;
        }

        private void Warn(string message) {
            MessageBox.Show(this, message, "Invalid Address", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _addressTextBox.Focus();
            _addressTextBox.SelectAll();
        }
    }

    private sealed class AddressLabelDialog : DarkDialog {
        private readonly TextBox _labelTextBox;
        public string AddressLabel => _labelTextBox.Text.Trim();

        public AddressLabelDialog(long address, string currentLabel) : base("Address Label", new Size(320, 120)) {
            AddLabel($"Enter Address Label for ${address:X6}:", bold: true);
            _labelTextBox = AddTextBox(new Point(16, 36), new Size(288, 23), Theme.UiInput, currentLabel);

            AcceptButton = AddButton("Save", new Point(148, 76), primary: true, DialogResult.OK);
            CancelButton = AddButton("Cancel", new Point(229, 76), primary: false, DialogResult.Cancel);

            Shown += (_, _) => {
                _labelTextBox.Focus();
                _labelTextBox.SelectAll();
            };
        }
    }

    private sealed class SelectColorDialog : DarkDialog {
        private readonly ComboBox _colorComboBox;
        public string SelectedColor => _colorComboBox.SelectedItem?.ToString() ?? "None";

        public SelectColorDialog(int cellCount, string initialColor) : base("Select Cell Color", new Size(300, 120)) {
            string target = cellCount > 1 ? $"{cellCount} cells" : "selected cell";
            AddLabel($"Select Color for {target}:", bold: true);

            _colorComboBox = new ComboBox {
                Location = new Point(16, 36),
                Size = new Size(268, 24),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.UiInput,
                BackColor = Theme.Back,
                ForeColor = Theme.Fore,
                FlatStyle = FlatStyle.Flat
            };

            string[] options = ["None", "Blue", "Red", "Green", "Orange", "Purple"];
            _colorComboBox.Items.AddRange(options);

            int index = Array.FindIndex(options, o => string.Equals(o, initialColor, StringComparison.OrdinalIgnoreCase));
            _colorComboBox.SelectedIndex = index >= 0 ? index : 1; // default to Blue when nothing is set
            Controls.Add(_colorComboBox);

            AcceptButton = AddButton("Apply", new Point(128, 76), primary: true, DialogResult.OK);
            CancelButton = AddButton("Cancel", new Point(209, 76), primary: false, DialogResult.Cancel);

            Shown += (_, _) => _colorComboBox.Focus();
        }
    }
}