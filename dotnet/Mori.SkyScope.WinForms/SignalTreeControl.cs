// Mori.SkyScope — Windows Forms signal tree: a searchable tree of channels grouped by name prefix, dragged onto a trend chart control.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.WinForms;

/// <summary>
/// A searchable tree of every channel in a store (grouped by name prefix). Nodes are ordinary Windows Forms drag sources
/// (<c>DoDragDrop</c> with a <see cref="ChannelDragData.Format"/> data object plus plain-text ids), so any
/// <see cref="TrendChartControl"/> accepts them and the drop obeys the chart's rules (axis strip → shared scale, lane →
/// own scale, time axis → new lane, logic stack for digital). Ctrl/Shift clicks pick several; a drag on a selected node
/// carries the selection as a group. Double-click on a channel adds it to the attached chart's first lane. The chart is
/// optional: without it the tree lists <see cref="Store"/> and drags.
/// </summary>
[ToolboxItem(true), Description("Searchable tree of the store's channels; drag rows onto a trend chart.")]
public class SignalTreeControl : UserControl
{
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "search signals…" };
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false, ShowLines = false, FullRowSelect = true, BorderStyle = BorderStyle.FixedSingle };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private SignalTreeModel? _model;
    private SignalStore? _store;
    private string _signature = "";
    private Point _pressAt;
    private TreeNode? _pressed;
    private bool _dragging;
    private readonly HashSet<int> _selected = [];

    /// <summary>Builds the search box and the tree and wires the mouse handling.</summary>
    public SignalTreeControl()
    {
        Controls.Add(_tree); Controls.Add(_search);
        _search.TextChanged += (_, _) => { if (_model is not null) { _model.SetQuery(_search.Text); RefreshRows(); } };
        _tree.MouseDown += OnPress; _tree.MouseMove += OnMove; _tree.MouseUp += OnRelease; _tree.NodeMouseDoubleClick += OnDoubleClick;
        _tree.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { _selected.Clear(); RefreshRows(); } };
        _tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
        _tree.DrawNode += DrawNode;
        _timer.Tick += (_, _) => { if (Signature() != _signature) RefreshRows(); };
    }

    /// <summary>Optional: a chart whose series are marked in the tree and that double-click adds to; its store is listed unless <see cref="Store"/> is set. Drops work on any chart without it.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public TrendChartControl? Chart { get; set; }
    /// <summary>Store to list; defaults to the chart's.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public SignalStore? Store { get => _store; set { _store = value; _model = null; RefreshRows(); } }
    /// <summary>Characters that split a channel name into group and leaf.</summary>
    [Category("Behavior"), DefaultValue("/.:")]
    public string Separators { get; set; } = "/.:";
    /// <summary>The tree model (rows, search, selection).</summary>
    [Browsable(false)] public SignalTreeModel? Model => _model;

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); if (!DesignMode) _timer.Start(); }
    protected override void OnHandleDestroyed(EventArgs e) { _timer.Stop(); base.OnHandleDestroyed(e); }

    private SignalTreeModel? EnsureModel()
    {
        var store = _store ?? Chart?.Store;
        if (store is null) return null;
        if (_model is null || _model.Store != store) { _model = new SignalTreeModel(store, Separators); _model.SetQuery(_search.Text); }
        return _model;
    }

    private string Signature()
    {
        var store = _store ?? Chart?.Store;
        return $"{store?.Channels.Count ?? 0}:{string.Join(",", Chart?.Model.Config.Series.Select(s => s.ChannelId) ?? [])}";
    }

    /// <summary>Rebuilds the nodes from the model.</summary>
    public void RefreshRows()
    {
        var m = EnsureModel();
        _signature = Signature();
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        if (m is not null)
        {
            var inChart = new HashSet<int>(Chart?.Model.Config.Series.Select(s => s.ChannelId) ?? []);
            List<SignalTreeRow> rows;
            lock (m.Store.SyncRoot) rows = m.Rows();
            TreeNode? group = null;
            foreach (var r in rows)
            {
                var text = r.Kind == SignalTreeRowKind.Group ? $"{r.Name}  ({r.Count})" : r.Unit is null ? r.Name : $"{r.Name}  [{r.Unit}]";
                if (r.ChannelId is { } id && inChart.Contains(id)) text += "  ●";
                var node = new TreeNode(text) { Tag = r, Name = r.Id };
                if (r.Kind == SignalTreeRowKind.Group) { group = node; _tree.Nodes.Add(node); if (r.Expanded == true) node.Expand(); }
                else if (r.Depth > 0 && group is not null) group.Nodes.Add(node);
                else _tree.Nodes.Add(node);
            }
            foreach (TreeNode n in _tree.Nodes) if (n.Tag is SignalTreeRow { Kind: SignalTreeRowKind.Group, Expanded: true }) n.Expand();
        }
        _tree.EndUpdate();
    }

    private void DrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        var row = e.Node?.Tag as SignalTreeRow;
        var selected = row?.ChannelId is { } id && _selected.Contains(id);
        var back = selected ? SystemColors.Highlight : _tree.BackColor;
        var fore = selected ? SystemColors.HighlightText : row?.Kind == SignalTreeRowKind.Group ? SystemColors.GrayText : _tree.ForeColor;
        using var b = new SolidBrush(back);
        e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, e.Node?.Text ?? "", row?.Kind == SignalTreeRowKind.Group ? new Font(_tree.Font, FontStyle.Bold) : _tree.Font, e.Bounds, fore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    private void OnPress(object? sender, MouseEventArgs e)
    {
        _pressed = _tree.GetNodeAt(e.Location); _pressAt = e.Location; _dragging = false;
        if (_pressed?.Tag is not SignalTreeRow row || EnsureModel() is not { } model) return;
        var ctrl = ModifierKeys.HasFlag(Keys.Control); var shift = ModifierKeys.HasFlag(Keys.Shift);
        if (row.Kind == SignalTreeRowKind.Group) { if (!ctrl && !shift) { model.ToggleGroup(row.Id[2..]); } else model.Click(row.Id, ctrl, shift); }
        else
        {
            // the model keeps the Ctrl/Shift selection; a plain press on a selected row keeps it so a drag can carry the group
            if (!(row.ChannelId is { } cid && _selected.Contains(cid) && !ctrl && !shift)) model.Click(row.Id, ctrl, shift);
        }
        _selected.Clear(); foreach (var id in model.Selection) _selected.Add(id);
        RefreshRows();
    }

    /// <summary>The payload a drag of <paramref name="channelId"/> carries: the selection when the node is selected, the node alone otherwise.</summary>
    public ChannelDragPayload DragPayload(int channelId, bool group = false)
    {
        var m = EnsureModel();
        var ids = m?.DragIds(channelId) ?? [channelId];
        var store = _store ?? Chart?.Store;
        return new ChannelDragPayload(ids.Select(id => { var info = store?.Get(id)?.Info; return new ChannelDragItem(id, info?.Name, info?.Unit, info?.Kind == ChannelKind.Digital ? ChannelKind.Digital : ChannelKind.Analog); }).ToList(), group || ids.Count > 1);
    }

    /// <summary>The data object a drag carries: the payload under <see cref="ChannelDragData.Format"/> and the ids as text.</summary>
    public static DataObject ToDataObject(ChannelDragPayload payload)
    {
        var data = new DataObject();
        data.SetData(ChannelDragData.Format, ChannelDragData.Encode(payload));
        data.SetData(DataFormats.UnicodeText, string.Join(",", payload.Ids));
        return data;
    }

    private void OnMove(object? sender, MouseEventArgs e)
    {
        if (_pressed is null || e.Button != MouseButtons.Left || _dragging) return;
        if (_pressed.Tag is not SignalTreeRow { Kind: SignalTreeRowKind.Channel, ChannelId: { } id }) return;
        if (Math.Abs(e.X - _pressAt.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - _pressAt.Y) < SystemInformation.DragSize.Height / 2) return;
        var payload = DragPayload(id, ModifierKeys.HasFlag(Keys.Control) || ModifierKeys.HasFlag(Keys.Shift));
        _dragging = true;
        try { _tree.DoDragDrop(ToDataObject(payload), DragDropEffects.Copy); }
        finally { _dragging = false; _pressed = null; RefreshRows(); }
    }

    private void OnRelease(object? sender, MouseEventArgs e) => _pressed = null;

    private void OnDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node?.Tag is not SignalTreeRow { Kind: SignalTreeRowKind.Channel, ChannelId: { } id } || Chart is null) return;
        Chart.AddChannels([id]);
        RefreshRows();
    }

    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
