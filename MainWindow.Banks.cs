using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace DirOpusReImagined
{
    /// <summary>
    /// Banks of command buttons, as in Directory Opus: the 36 buttons at the bottom show one bank at
    /// a time. Switch with the bank tabs above them, the mouse wheel over them, or Alt+PageUp/PageDown
    /// in a panel. Banks are edited in the button editor and saved in Configuration.xml, along with
    /// which one was showing.
    /// </summary>
    public partial class MainWindow
    {
        private List<ButtonBank> _banks = new() { new ButtonBank(ButtonBanks.DefaultName(0)) };
        private int _bankIndex;
        private bool _banksLoaded;

        private void InitButtonBanks()
        {
            EditBanksButton.Click += (_, _) => OpenButtonEditor();
            ButtonGrid.PointerWheelChanged += (_, e) =>
            {
                if (_banks.Count < 2 || e.Delta.Y == 0) return;
                StepButtonBank(e.Delta.Y < 0 ? +1 : -1);
                e.Handled = true;
            };
            Closing += (_, _) => SaveActiveBankToConfig();
            RebuildBankTabs();
        }

        /// <summary>Reads the banks from the config at start-up and shows the one that was showing last time.</summary>
        private void LoadButtonBanks(XElement? root)
        {
            _banks = ButtonBanks.Read(root);
            _bankIndex = _banksLoaded ? Math.Clamp(_bankIndex, 0, _banks.Count - 1) : ButtonBanks.ReadActive(root, _banks.Count);
            _banksLoaded = true;
            ShowButtonBank(_bankIndex);
        }

        /// <summary>Takes the banks the editor just saved and shows <paramref name="index"/>.</summary>
        public void SetButtonBanks(IEnumerable<ButtonBank> banks, int index)
        {
            _banks = banks.Select(b => b.Clone()).ToList();
            if (_banks.Count == 0) _banks.Add(new ButtonBank(ButtonBanks.DefaultName(0)));
            ShowButtonBank(Math.Clamp(index, 0, _banks.Count - 1));
        }

        private void StepButtonBank(int delta)
        {
            if (_banks.Count < 2) return;
            ShowButtonBank(ButtonBanks.Step(_bankIndex, delta, _banks.Count));
        }

        private void OpenButtonEditor()
        {
            KillPop();
            var editor = new AddEditCmdButtonDefinition(_banks, _bankIndex) { TheMainWindow = this };
            editor.ShowDialog(this);
        }

        private Button? CommandButton(int slot) => ButtonGrid.FindControl<Button>($"LpButton{slot}");

        /// <summary>Puts bank <paramref name="index"/> on the 36 buttons, resetting any the bank leaves empty.</summary>
        private void ShowButtonBank(int index)
        {
            _bankIndex = index;
            TheButtons.Clear();
            KillPop();

            for (int slot = 1; slot <= ButtonBanks.ButtonsPerBank; slot++)
                if (CommandButton(slot) is { } b) ResetCommandButton(b);

            foreach (var settings in _banks[index].Buttons)
            {
                int slot = ButtonBanks.SlotOf(settings.Name);
                if (slot == 0 || CommandButton(slot) is not { } button) continue;
                try
                {
                    ApplyCommandButton(button, settings);
                }
                catch (Exception ex)
                {
                    // One bad colour or margin shouldn't stop the rest of the bank from showing.
                    Console.WriteLine($"Button {settings.Name}: {ex.Message}");
                }
            }
            RebuildBankTabs();
        }

        private static void ResetCommandButton(Button b)
        {
            b.Content = "";
            b.Tag = null;
            b.ClearValue(TemplatedControl.BackgroundProperty);
            b.ClearValue(TemplatedControl.ForegroundProperty);
            b.ClearValue(ContentControl.HorizontalContentAlignmentProperty);
            b.ClearValue(ContentControl.VerticalContentAlignmentProperty);
            b.Margin = new Thickness(1);
            ToolTip.SetIsOpen(b, false);
            ToolTip.SetTip(b, null);
        }

        private void ApplyCommandButton(Button button, ButtonSettings s)
        {
            s.Name = $"LpButton{ButtonBanks.SlotOf(s.Name)}";   // the click handler matches on this
            button.Tag = s;
            // A TextBlock rather than a plain string so a label too long for its button ends in "…"
            // instead of being cut off mid-letter. Foreground is inherited from the button.
            if (!string.IsNullOrEmpty(s.Content))
                button.Content = new TextBlock { Text = s.Content, TextTrimming = TextTrimming.CharacterEllipsis };
            if (!string.IsNullOrEmpty(s.Background)) button.Background = new SolidColorBrush(Color.Parse(s.Background));
            if (!string.IsNullOrEmpty(s.Foreground)) button.Foreground = new SolidColorBrush(Color.Parse(s.Foreground));

            switch (s.HorizontalAlignment?.ToUpperInvariant())
            {
                case "LEFT": button.HorizontalContentAlignment = HorizontalAlignment.Left; break;
                case "CENTER": button.HorizontalContentAlignment = HorizontalAlignment.Center; break;
                case "RIGHT": button.HorizontalContentAlignment = HorizontalAlignment.Right; break;
            }
            switch (s.VerticalAlignment?.ToUpperInvariant())
            {
                case "TOP": button.VerticalContentAlignment = VerticalAlignment.Top; break;
                case "CENTER": button.VerticalContentAlignment = VerticalAlignment.Center; break;
                case "BOTTOM": button.VerticalContentAlignment = VerticalAlignment.Bottom; break;
            }
            if (!string.IsNullOrEmpty(s.Margin)) button.Margin = Thickness.Parse(s.Margin);

            if (string.IsNullOrEmpty(s.Action)) return;
            TheButtons.Add(ToEntry(s));
        }

        /// <summary>One tab per bank above the buttons, the showing one pressed in.</summary>
        private void RebuildBankTabs()
        {
            BankTabs.Children.Clear();
            for (int i = 0; i < _banks.Count; i++)
            {
                int index = i;
                var tab = new ToggleButton
                {
                    Content = _banks[i].Name,
                    IsChecked = i == _bankIndex,
                    FontSize = 11,
                    Padding = new Thickness(10, 0),
                    VerticalAlignment = VerticalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                ToolTip.SetTip(tab, "Alt+PageUp / Alt+PageDown, or the mouse wheel over the buttons, also switch banks");
                // A ToggleButton unpresses itself on a second click; keep the showing bank pressed.
                tab.Click += (_, _) => ShowButtonBank(index);
                BankTabs.Children.Add(tab);
            }
        }

        private void SaveActiveBankToConfig()
        {
            try
            {
                string path = _configFilePath ?? GetWritableConfigPath();
                if (!File.Exists(path)) return;
                XDocument doc = XDocument.Load(path);
                if (doc.Root == null) return;
                ButtonBanks.WriteActive(doc.Root, _bankIndex);
                doc.Save(path);
            }
            catch { /* best-effort, like the other settings saved on close */ }
        }
    }
}
