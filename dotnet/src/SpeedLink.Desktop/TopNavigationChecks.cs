using System.IO;
using System.Windows;
using System.Windows.Controls;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    private async Task CheckSharedTopNavigation(string folder)
    {
        var sections = Navigation.Children.OfType<RadioButton>().ToArray();
        AdapterBox.SelectedIndex = 0; PortBox.Text = "COM_SYNTHETIC";
        using var peer = new SharedSessionPeer();
        offlineConnectionFactory = () => (peer, null, new("synthetic", "Synthetic shared adapter", "JM1GG000000000001", "L3M6EG000L3T60A1"));
        try
        {
            Navigate(1); ConnectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await activeWork!; await MenuIdle();
            if (!ReferenceEquals(transport, peer) || Pages.SelectedIndex != 1 || !DisconnectButton.IsVisible)
                throw new InvalidOperationException("Shared Connect left the selected section or did not retain its session.");
            var connectedIdentity = identity;
            foreach (int page in Enumerable.Range(0, Pages.Items.Count))
            {
                Navigate(page); await MenuIdle();
                if (!ReferenceEquals(transport, peer) || !ReferenceEquals(identity, connectedIdentity) || peer.Disposals != 0 ||
                    !sections.Single(b => Equals(b.Tag, SectionFor(page))).IsChecked.GetValueOrDefault())
                    throw new InvalidOperationException("Changing sections lost the connection or selected the wrong group.");
            }
            Navigate(4); Navigate(5); Navigate(3);
            foreach (var (group, expected) in new[] { ("editor", 4), ("ecu", 5), ("logging", 3) })
            {
                sections.Single(b => Equals(b.Tag, group)).IsChecked = true; await MenuIdle();
                if (Pages.SelectedIndex != expected) throw new InvalidOperationException("A section forgot its last view.");
            }
            busy = true; loggingActive = true; StatusText.Text = "Logging · synthetic acceptance session"; Controls();
            try
            {
                Navigate(1); await MenuIdle();
                if (!ActivityPanel.IsVisible || !StopButton.IsEnabled || ConnectionSettingsButton.IsEnabled || DisconnectButton.IsEnabled)
                    throw new InvalidOperationException("Shared activity and operation locks did not follow navigation.");
                bool rejected = false;
                try { await PrepareSharedProgramming(); } catch (InvalidOperationException) { rejected = true; }
                if (!rejected || peer.Disposals != 0) throw new InvalidOperationException("Programming handoff interrupted an active operation.");
            }
            finally { loggingActive = false; busy = false; Controls(); }
            Navigate(6); await PrepareSharedProgramming(); await MenuIdle();
            if (peer.Disposals != 1 || transport is not null || !ConnectButton.IsEnabled || Pages.SelectedIndex != 6)
                throw new InvalidOperationException("Programming handoff did not close the ordinary session exactly once, in place.");
            var priorAppearance = appearance;
            try
            {
                foreach (var theme in new[] { Appearance.Light, Appearance.Dark })
                {
                    UiTheme.Apply(theme, greenForHighValues);
                    Width = MinWidth; Height = MinHeight;
                    foreach (int page in new[] { 7, 1, 2, 6 })
                    {
                        Navigate(page); await MenuIdle();
                        foreach (FrameworkElement control in sections.Cast<FrameworkElement>().Append(ConnectionSettingsButton).Append(ConnectButton))
                        {
                            var bounds = control.TransformToAncestor(this).TransformBounds(new Rect(control.RenderSize));
                            if (!control.IsVisible || bounds.Left < 0 || bounds.Right > ActualWidth - 3 || bounds.Bottom > 125)
                                throw new InvalidOperationException("Top-level controls clip at the minimum window size.");
                        }
                        CaptureWorkbench(Path.Combine(folder, $"TopNavigation-{theme}-{SectionFor(page)}.png"));
                    }
                }
                SharedConnectionPopup.IsOpen = true; await MenuIdle();
                if (!AdapterBox.IsVisible || !PortBox.IsVisible || !BaudBox.IsVisible)
                    throw new InvalidOperationException("The shared settings popup is missing adapter controls.");
                if (SharedConnectionPopup.Child is System.Windows.Controls.Border surface)
                    EditorWorkspace.SaveMenuImage(surface, Path.Combine(folder, "SharedConnection.png"));
            }
            finally { SharedConnectionPopup.IsOpen = false; UiTheme.Apply(priorAppearance, greenForHighValues); }
        }
        finally
        {
            offlineConnectionFactory = null;
            if (transport is not null) await Disconnect();
            Controls();
        }
    }
    private sealed class SharedSessionPeer : IReadTransport
    {
        public string Mode => "synthetic";
        public string Adapter => "Synthetic shared adapter";
        public int ReadChunkSize => 256;
        public bool ReadAttempted { get; set; }
        public int Disposals { get; private set; }
        public byte[] Exchange(byte[] request) => throw new InvalidOperationException("Shell acceptance must not issue ECU requests.");
        public void Dispose() => Disposals++;
    }
}
