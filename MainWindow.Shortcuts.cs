using System.Linq;
using System.Xml.Linq;
using Avalonia.Threading;

namespace DirOpusReImagined
{
    /// <summary>Applying the &lt;Shortcuts&gt; section of Configuration.xml (see <see cref="KeyMap"/>).</summary>
    public partial class MainWindow
    {
        private void LoadShortcuts(XElement? root)
        {
            KeyMap.Current = KeyMap.FromXml(root, out var problems);
            if (problems.Count == 0) return;

            // Say what was skipped, once the window is up, rather than silently ignoring a typo.
            string list = string.Join("\n", problems.Take(10));
            if (problems.Count > 10) list += $"\n… and {problems.Count - 10} more.";
            Dispatcher.UIThread.Post(() => _ = new MessageBox(
                "Some keyboard shortcuts in Configuration.xml were skipped:\n\n" + list +
                "\n\nThe other shortcuts work as usual. General Help lists the command names.",
                "Keyboard shortcuts").ShowDialog(this), DispatcherPriority.Background);
        }
    }
}
