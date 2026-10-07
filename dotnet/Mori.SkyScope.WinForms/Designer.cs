// Mori.SkyScope — Designer-friendly definitions of lanes, axes, series, thresholds and markers for the Windows Forms trend chart.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Collections.ObjectModel;
using System.ComponentModel;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Scales;

namespace Mori.SkyScope.WinForms;

/// <summary>Light or dark chart theme, as a designer-friendly choice.</summary>
public enum ChartThemeChoice
{
    /// <summary><see cref="ChartTheme.Light"/>.</summary>
    Light,
    /// <summary><see cref="ChartTheme.Dark"/>.</summary>
    Dark,
}

/// <summary>A lane as the designer edits it; becomes a <see cref="LaneConfig"/>.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class LaneDefinition : DefinitionItem
{
    /// <summary>Lane id, referenced by series.</summary>
    private string _id = "lane";
    [Description("Lane id, referenced by series.")] public string Id { get => _id; set { _id = value; Notify(); } }
    /// <summary>Optional caption.</summary>
    private string? _label;
    [DefaultValue(null)] public string? Label { get => _label; set { _label = value; Notify(); } }
    /// <summary>Share of the plot height relative to the other lanes.</summary>
    private double _weight = 1;
    [DefaultValue(1.0)] public double Weight { get => _weight; set { _weight = value; Notify(); } }
    /// <summary>Start folded to a summary bar.</summary>
    private bool _collapsed;
    [DefaultValue(false)] public bool Collapsed { get => _collapsed; set { _collapsed = value; Notify(); } }
    /// <inheritdoc/>
    public override string ToString() => $"{Id} (×{Weight})";
    internal LaneConfig ToConfig() => new(Id) { Label = Label, Weight = Weight, Collapsed = Collapsed };
}

/// <summary>An axis as the designer edits it; becomes an <see cref="AxisConfig"/>. Leave a bound NaN for autoscale.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class AxisDefinition : DefinitionItem
{
    /// <summary>Axis id, referenced by series and thresholds.</summary>
    private string _id = "axis";
    public string Id { get => _id; set { _id = value; Notify(); } }
    /// <summary>Caption drawn at the top of the strip.</summary>
    private string? _label;
    [DefaultValue(null)] public string? Label { get => _label; set { _label = value; Notify(); } }
    /// <summary>Unit shown in the legend.</summary>
    private string? _unit;
    [DefaultValue(null)] public string? Unit { get => _unit; set { _unit = value; Notify(); } }
    /// <summary>Fixed lower bound; NaN autoscales.</summary>
    private double _min = double.NaN;
    [DefaultValue(double.NaN)] public double Min { get => _min; set { _min = value; Notify(); } }
    /// <summary>Fixed upper bound; NaN autoscales.</summary>
    private double _max = double.NaN;
    [DefaultValue(double.NaN)] public double Max { get => _max; set { _max = value; Notify(); } }
    /// <summary>Left or right column.</summary>
    private AxisSide _side = AxisSide.Left;
    [DefaultValue(AxisSide.Left)] public AxisSide Side { get => _side; set { _side = value; Notify(); } }
    /// <inheritdoc/>
    public override string ToString() => Id;
    internal AxisConfig ToConfig() => new(Id) { Label = Label, Unit = Unit, Min = double.IsNaN(Min) ? null : Min, Max = double.IsNaN(Max) ? null : Max, Side = Side };
}

/// <summary>A series as the designer edits it; becomes a <see cref="SeriesConfig"/>.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class SeriesDefinition : DefinitionItem
{
    /// <summary>Series id (unique in the chart).</summary>
    private string _id = "s1";
    public string Id { get => _id; set { _id = value; Notify(); } }
    /// <summary>Channel of the store this series draws.</summary>
    private int _channelId = 1;
    [DefaultValue(1)] public int ChannelId { get => _channelId; set { _channelId = value; Notify(); } }
    /// <summary>Lane id; empty = the first lane.</summary>
    private string? _laneId;
    [DefaultValue(null)] public string? LaneId { get => _laneId; set { _laneId = value; Notify(); } }
    /// <summary>Axis id; empty = the lane's default axis.</summary>
    private string? _axisId;
    [DefaultValue(null)] public string? AxisId { get => _axisId; set { _axisId = value; Notify(); } }
    /// <summary>Display name; empty = the channel name.</summary>
    private string? _name;
    [DefaultValue(null)] public string? Name { get => _name; set { _name = value; Notify(); } }
    /// <summary>CSS colour; empty = palette.</summary>
    private string? _color;
    [DefaultValue(null)] public string? Color { get => _color; set { _color = value; Notify(); } }
    /// <summary>Line width in pixels; 0 = style default.</summary>
    private double _width;
    [DefaultValue(0.0)] public double Width { get => _width; set { _width = value; Notify(); } }
    /// <summary>Shown or hidden.</summary>
    private bool _visible = true;
    [DefaultValue(true)] public bool Visible { get => _visible; set { _visible = value; Notify(); } }
    /// <summary>Logic-analyzer track instead of an analog line.</summary>
    private bool _digital;
    [DefaultValue(false)] public bool Digital { get => _digital; set { _digital = value; Notify(); } }
    /// <inheritdoc/>
    public override string ToString() => $"{Id} ← ch {ChannelId}";
    internal SeriesConfig ToConfig() => new(Id, ChannelId)
    {
        LaneId = string.IsNullOrWhiteSpace(LaneId) ? null : LaneId, AxisId = string.IsNullOrWhiteSpace(AxisId) ? null : AxisId, Name = string.IsNullOrWhiteSpace(Name) ? null : Name,
        Color = string.IsNullOrWhiteSpace(Color) ? null : Color, Width = Width > 0 ? Width : null, Visible = Visible, Kind = Digital ? SeriesKind.Digital : SeriesKind.Analog,
    };
}

/// <summary>A threshold as the designer edits it; becomes a <see cref="ThresholdConfig"/>. Leave <see cref="To"/> NaN for a line.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class ThresholdDefinition : DefinitionItem
{
    /// <summary>Threshold id.</summary>
    private string _id = "t1";
    public string Id { get => _id; set { _id = value; Notify(); } }
    /// <summary>Axis the values refer to.</summary>
    private string _axisId = "axis";
    public string AxisId { get => _axisId; set { _axisId = value; Notify(); } }
    /// <summary>Line value, or the band's lower bound.</summary>
    private double _from;
    public double From { get => _from; set { _from = value; Notify(); } }
    /// <summary>Band's upper bound; NaN draws a line.</summary>
    private double _to = double.NaN;
    [DefaultValue(double.NaN)] public double To { get => _to; set { _to = value; Notify(); } }
    /// <summary>CSS colour.</summary>
    private string _color = "#dc2626";
    public string Color { get => _color; set { _color = value; Notify(); } }
    /// <summary>Optional caption.</summary>
    private string? _label;
    [DefaultValue(null)] public string? Label { get => _label; set { _label = value; Notify(); } }
    /// <inheritdoc/>
    public override string ToString() => $"{Id} @ {From}";
    internal ThresholdConfig ToConfig() => new(Id, AxisId, From, Color) { To = double.IsNaN(To) ? null : To, Label = Label };
}

/// <summary>An event marker as the designer edits it; becomes a <see cref="MarkerConfig"/>.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class MarkerDefinition : DefinitionItem
{
    /// <summary>Marker id.</summary>
    private string _id = "m1";
    public string Id { get => _id; set { _id = value; Notify(); } }
    /// <summary>Chart time in seconds.</summary>
    private double _time;
    public double Time { get => _time; set { _time = value; Notify(); } }
    /// <summary>Optional caption.</summary>
    private string? _label;
    [DefaultValue(null)] public string? Label { get => _label; set { _label = value; Notify(); } }
    /// <summary>CSS colour; empty = theme marker colour.</summary>
    private string? _color;
    [DefaultValue(null)] public string? Color { get => _color; set { _color = value; Notify(); } }
    /// <inheritdoc/>
    public override string ToString() => $"{Id} @ {Time}";
    internal MarkerConfig ToConfig() => new(Id, Time) { Label = Label, Color = string.IsNullOrWhiteSpace(Color) ? null : Color };
}

/// <summary>Base of the designer items: raises <see cref="Changed"/> when a property is set, so a chart can rebuild while the collection editor edits an item.</summary>
public abstract class DefinitionItem
{
    /// <summary>Raised after any property changed.</summary>
    public event Action? Changed;
    /// <summary>Raises <see cref="Changed"/>.</summary>
    protected void Notify() => Changed?.Invoke();
}

/// <summary>Collections the designer serialises item by item; <see cref="Changed"/> fires when items are added, removed or replaced, and when an item's property changes.</summary>
public sealed class DefinitionCollection<T> : Collection<T> where T : DefinitionItem, new()
{
    /// <summary>Raised after the collection or one of its items changed.</summary>
    public event Action? Changed;
    private void Raise() => Changed?.Invoke();
    /// <inheritdoc/>
    protected override void InsertItem(int index, T item) { base.InsertItem(index, item); item.Changed += Raise; Raise(); }
    /// <inheritdoc/>
    protected override void RemoveItem(int index) { this[index].Changed -= Raise; base.RemoveItem(index); Raise(); }
    /// <inheritdoc/>
    protected override void SetItem(int index, T item) { this[index].Changed -= Raise; base.SetItem(index, item); item.Changed += Raise; Raise(); }
    /// <inheritdoc/>
    protected override void ClearItems() { foreach (var it in this) it.Changed -= Raise; base.ClearItems(); Raise(); }
}
