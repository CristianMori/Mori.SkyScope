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
public sealed class LaneDefinition
{
    /// <summary>Lane id, referenced by series.</summary>
    [Description("Lane id, referenced by series.")] public string Id { get; set; } = "lane";
    /// <summary>Optional caption.</summary>
    public string? Label { get; set; }
    /// <summary>Share of the plot height relative to the other lanes.</summary>
    [DefaultValue(1.0)] public double Weight { get; set; } = 1;
    /// <summary>Start folded to a summary bar.</summary>
    [DefaultValue(false)] public bool Collapsed { get; set; }
    /// <inheritdoc/>
    public override string ToString() => $"{Id} (×{Weight})";
    internal LaneConfig ToConfig() => new(Id) { Label = Label, Weight = Weight, Collapsed = Collapsed };
}

/// <summary>An axis as the designer edits it; becomes an <see cref="AxisConfig"/>. Leave a bound NaN for autoscale.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class AxisDefinition
{
    /// <summary>Axis id, referenced by series and thresholds.</summary>
    public string Id { get; set; } = "axis";
    /// <summary>Caption drawn at the top of the strip.</summary>
    public string? Label { get; set; }
    /// <summary>Unit shown in the legend.</summary>
    public string? Unit { get; set; }
    /// <summary>Fixed lower bound; NaN autoscales.</summary>
    [DefaultValue(double.NaN)] public double Min { get; set; } = double.NaN;
    /// <summary>Fixed upper bound; NaN autoscales.</summary>
    [DefaultValue(double.NaN)] public double Max { get; set; } = double.NaN;
    /// <summary>Left or right column.</summary>
    [DefaultValue(AxisSide.Left)] public AxisSide Side { get; set; } = AxisSide.Left;
    /// <inheritdoc/>
    public override string ToString() => Id;
    internal AxisConfig ToConfig() => new(Id) { Label = Label, Unit = Unit, Min = double.IsNaN(Min) ? null : Min, Max = double.IsNaN(Max) ? null : Max, Side = Side };
}

/// <summary>A series as the designer edits it; becomes a <see cref="SeriesConfig"/>.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class SeriesDefinition
{
    /// <summary>Series id (unique in the chart).</summary>
    public string Id { get; set; } = "s1";
    /// <summary>Channel of the store this series draws.</summary>
    [DefaultValue(1)] public int ChannelId { get; set; } = 1;
    /// <summary>Lane id; empty = the first lane.</summary>
    public string? LaneId { get; set; }
    /// <summary>Axis id; empty = the lane's default axis.</summary>
    public string? AxisId { get; set; }
    /// <summary>Display name; empty = the channel name.</summary>
    public string? Name { get; set; }
    /// <summary>CSS colour; empty = palette.</summary>
    public string? Color { get; set; }
    /// <summary>Line width in pixels; 0 = style default.</summary>
    [DefaultValue(0.0)] public double Width { get; set; }
    /// <summary>Shown or hidden.</summary>
    [DefaultValue(true)] public bool Visible { get; set; } = true;
    /// <summary>Logic-analyzer track instead of an analog line.</summary>
    [DefaultValue(false)] public bool Digital { get; set; }
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
public sealed class ThresholdDefinition
{
    /// <summary>Threshold id.</summary>
    public string Id { get; set; } = "t1";
    /// <summary>Axis the values refer to.</summary>
    public string AxisId { get; set; } = "axis";
    /// <summary>Line value, or the band's lower bound.</summary>
    public double From { get; set; }
    /// <summary>Band's upper bound; NaN draws a line.</summary>
    [DefaultValue(double.NaN)] public double To { get; set; } = double.NaN;
    /// <summary>CSS colour.</summary>
    public string Color { get; set; } = "#dc2626";
    /// <summary>Optional caption.</summary>
    public string? Label { get; set; }
    /// <inheritdoc/>
    public override string ToString() => $"{Id} @ {From}";
    internal ThresholdConfig ToConfig() => new(Id, AxisId, From, Color) { To = double.IsNaN(To) ? null : To, Label = Label };
}

/// <summary>An event marker as the designer edits it; becomes a <see cref="MarkerConfig"/>.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class MarkerDefinition
{
    /// <summary>Marker id.</summary>
    public string Id { get; set; } = "m1";
    /// <summary>Chart time in seconds.</summary>
    public double Time { get; set; }
    /// <summary>Optional caption.</summary>
    public string? Label { get; set; }
    /// <summary>CSS colour; empty = theme marker colour.</summary>
    public string? Color { get; set; }
    /// <inheritdoc/>
    public override string ToString() => $"{Id} @ {Time}";
    internal MarkerConfig ToConfig() => new(Id, Time) { Label = Label, Color = string.IsNullOrWhiteSpace(Color) ? null : Color };
}

/// <summary>Collections the designer serialises item by item.</summary>
public sealed class DefinitionCollection<T> : Collection<T> where T : new() { }
