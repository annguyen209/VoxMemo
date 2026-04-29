using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VoxMemo.ViewModels;

namespace VoxMemo.Views;

public partial class MeetingsView : UserControl
{
    private Slider? _seekSlider;
    private bool _thumbWired;

    public MeetingsView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        WireUpSeekSlider();
    }

    private void WireUpSeekSlider()
    {
        _seekSlider = this.FindNameScope()?.Find("SeekSlider") as Slider;
        if (_seekSlider == null) return;

        // Handle track clicks via ValueChanged when not dragging
        _seekSlider.ValueChanged += OnSliderValueChanged;

        // Wire thumb for drag detection
        _seekSlider.TemplateApplied += OnSliderTemplateApplied;
    }

    private void OnSliderTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (_thumbWired || _seekSlider == null) return;

        var thumb = _seekSlider.FindDescendantOfType<Thumb>();
        if (thumb == null) return;

        thumb.DragStarted += (_, _) =>
        {
            if (_seekSlider.DataContext is MeetingItemViewModel vm)
                vm.Playback.BeginSeek();
        };
        thumb.DragCompleted += (_, _) =>
        {
            if (_seekSlider.DataContext is MeetingItemViewModel vm)
                vm.Playback.EndSeek();
        };
        _thumbWired = true;
    }

    private void OnSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        // Only handle user-initiated track clicks (not thumb drags or programmatic updates)
        if (_seekSlider?.DataContext is MeetingItemViewModel vm && vm.Playback.IsPlaybackActive)
        {
            // If the change is large (>1 second jump), it's likely a track click
            var delta = Math.Abs(e.NewValue - e.OldValue);
            if (delta > 1.0)
            {
                vm.Playback.SeekTo(e.NewValue);
            }
        }
    }

    private void OnSegmentSelected(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is VoxMemo.ViewModels.SegmentItemViewModel seg &&
            DataContext is VoxMemo.ViewModels.MeetingsViewModel vm &&
            vm.SelectedMeeting?.Playback.IsPlaybackActive == true)
        {
            vm.SelectedMeeting.Playback.SeekTo(seg.StartSeconds);
        }
    }

    private void OnTitleLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.TextBox tb &&
            DataContext is VoxMemo.ViewModels.MeetingsViewModel vm &&
            vm.SelectedMeeting != null)
        {
            _ = vm.SelectedMeeting.Detail.SaveTitleCommand.ExecuteAsync(tb.Text);
        }
    }

    private void OnTitleKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (sender is not Avalonia.Controls.TextBox tb) return;
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            if (DataContext is VoxMemo.ViewModels.MeetingsViewModel vm && vm.SelectedMeeting != null)
                _ = vm.SelectedMeeting.Detail.SaveTitleCommand.ExecuteAsync(tb.Text);
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            if (DataContext is VoxMemo.ViewModels.MeetingsViewModel vm && vm.SelectedMeeting != null)
                tb.Text = vm.SelectedMeeting.Title;
        }
    }
}
