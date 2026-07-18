using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DirOpusReImagined.SystemInfo
{
    /// <summary>
    /// The optional user-configured terminal, read from the <c>&lt;Terminal&gt;</c> element of
    /// Configuration.xml:
    /// <code>
    ///   &lt;Terminal&gt;
    ///     &lt;Command&gt;wt.exe&lt;/Command&gt;
    ///     &lt;Args&gt;-d "%DIR%"&lt;/Args&gt;
    ///   &lt;/Terminal&gt;
    /// </code>
    /// <c>%DIR%</c> in the arguments is replaced with the target folder. When
    /// <see cref="Command"/> is empty the app falls back to built-in per-OS detection.
    /// </summary>
    public sealed class TerminalConfig
    {
        public string? Command { get; init; }
        public string? Args { get; init; }

        /// <summary>True when a non-empty custom command is configured.</summary>
        public bool HasCommand => !string.IsNullOrWhiteSpace(Command);

        /// <summary>Reads the &lt;Terminal&gt; settings from <paramref name="configPath"/>.
        /// Returns an empty config (no command) if the file or element is missing/unreadable.</summary>
        public static TerminalConfig Load(string? configPath)
        {
            try
            {
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
                    return new TerminalConfig();

                var terminal = XDocument.Load(configPath).Descendants("Terminal").FirstOrDefault();
                if (terminal == null)
                    return new TerminalConfig();

                return new TerminalConfig
                {
                    Command = (string?)terminal.Element("Command"),
                    Args = (string?)terminal.Element("Args")
                };
            }
            catch
            {
                return new TerminalConfig();
            }
        }
    }
}
