using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Editing;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;

namespace AutoSettings.Agent.Editor;

/// <summary>
/// YAML editor for automations: syntax highlighting, catalog-driven autocomplete (as you type or with
/// Ctrl+Space), hover help, and squiggles for problems reported by the validator.
/// </summary>
public sealed class YamlEditor : UserControl
{
    private static readonly Lazy<IHighlightingDefinition?> Highlighting = new(LoadHighlighting);

    private readonly TextEditor _editor;
    private readonly IssueRenderer _issues;
    private readonly DispatcherTimer _debounce;
    private readonly ToolTip _toolTip = new();
    private CompletionWindow? _completion;
    private bool _settingText;

    public YamlEditor()
    {
        _editor = new TextEditor
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 13,
            ShowLineNumbers = true,
            SyntaxHighlighting = Highlighting.Value,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(4),
        };
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.Options.IndentationSize = 2;
        _editor.Options.EnableHyperlinks = false;
        _editor.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        _editor.SetResourceReference(Control.BackgroundProperty, "ControlFillColorDefaultBrush");
        _editor.SetResourceReference(TextEditor.LineNumbersForegroundProperty, "TextFillColorTertiaryBrush");

        _issues = new IssueRenderer();
        _editor.TextArea.TextView.BackgroundRenderers.Add(_issues);

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            TextChangedDebounced?.Invoke(this, EventArgs.Empty);
        };

        _editor.TextChanged += (_, _) =>
        {
            if (_settingText)
                return;
            Changed?.Invoke(this, EventArgs.Empty);
            _debounce.Stop();
            _debounce.Start();
        };
        _editor.TextArea.TextEntered += OnTextEntered;
        _editor.TextArea.TextEntering += OnTextEntering;
        _editor.TextArea.PreviewKeyDown += OnPreviewKeyDown;
        _editor.MouseHover += OnMouseHover;
        _editor.MouseHoverStopped += (_, _) => _toolTip.IsOpen = false;

        Content = _editor;
    }

    /// <summary>Raised on every edit.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised 500 ms after the user stops typing (validate here).</summary>
    public event EventHandler? TextChangedDebounced;

    /// <summary>Whether the text is a whole file, one automation or one profile.</summary>
    public YamlDocumentKind DocumentKind { get; set; } = YamlDocumentKind.File;

    /// <summary>Personal or machine file (filters suggestions).</summary>
    public ExecutionScope Scope { get; set; } = ExecutionScope.User;

    /// <summary>Profile ids defined outside the text (for snippets).</summary>
    public Func<IEnumerable<string>>? KnownProfiles { get; set; }

    /// <summary>The text.</summary>
    public string Text
    {
        get => _editor.Text;
        set
        {
            _settingText = true;
            _editor.Text = value;
            _settingText = false;
        }
    }

    /// <summary>Shows problems as squiggles (with the message on hover).</summary>
    public void SetIssues(IEnumerable<ConfigIssue> issues)
    {
        _issues.Issues = issues.Where(i => i.Location is not null).ToList();
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    /// <summary>Moves the caret to a position and focuses the editor.</summary>
    public void GoTo(int line, int column)
    {
        if (line < 1 || line > _editor.Document.LineCount)
            return;
        var documentLine = _editor.Document.GetLineByNumber(line);
        _editor.CaretOffset = Math.Min(documentLine.Offset + Math.Max(0, column - 1), documentLine.EndOffset);
        _editor.ScrollToLine(line);
        _editor.Focus();
    }

    /// <summary>Selects the first occurrence of <paramref name="text"/>.</summary>
    public void Find(string text)
    {
        var index = _editor.Text.IndexOf(text, StringComparison.Ordinal);
        if (index < 0)
            return;
        _editor.Select(index, text.Length);
        _editor.ScrollToLine(_editor.Document.GetLineByOffset(index).LineNumber);
        _editor.Focus();
    }

    private void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        if (_completion is not null || e.Text.Length != 1)
            return;
        var ch = e.Text[0];
        if (char.IsLetter(ch) || ch == ' ' || ch == '_')
            ShowCompletion(automatic: true);
    }

    private void OnTextEntering(object sender, TextCompositionEventArgs e)
    {
        if (_completion is null || e.Text.Length == 0)
            return;
        var ch = e.Text[0];
        if (ch is '\n' or '\t')
            _completion.CompletionList.RequestInsertion(e);
        else if (!char.IsLetterOrDigit(ch) && ch is not ('_' or '.' or '-'))
            _completion.Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            ShowCompletion(automatic: false);
        }
    }

    private void ShowCompletion(bool automatic)
    {
        var result = YamlAssist.Complete(_editor.Text, _editor.CaretOffset, Scope, DocumentKind, KnownProfiles?.Invoke());
        if (result.Items.Count == 0)
            return;
        // While typing a value, only pop up automatically right after "key: ".
        if (automatic && result.ReplaceStart == _editor.CaretOffset && result.Items[0].Kind == CompletionKind.Key)
            return;

        _completion = new CompletionWindow(_editor.TextArea)
        {
            StartOffset = result.ReplaceStart,
            EndOffset = _editor.CaretOffset,
            MinWidth = 320,
        };
        foreach (var item in result.Items)
            _completion.CompletionList.CompletionData.Add(new CompletionData(item));
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
    }

    private void OnMouseHover(object sender, MouseEventArgs e)
    {
        var position = _editor.GetPositionFromPoint(e.GetPosition(_editor));
        if (position is null)
            return;
        var offset = _editor.Document.GetOffset(position.Value.Location);
        var line = position.Value.Line;

        var messages = _issues.Issues.Where(i => i.Location!.Value.Line == line).Select(i => i.Message).ToList();
        var hover = YamlAssist.Hover(_editor.Text, offset, DocumentKind);
        if (messages.Count == 0 && hover is null)
            return;

        var panel = new StackPanel { MaxWidth = 480 };
        foreach (var message in messages)
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed, Margin = new Thickness(0, 0, 0, 4) });
        if (hover is not null)
        {
            panel.Children.Add(new TextBlock { Text = hover.Title, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = hover.Description, TextWrapping = TextWrapping.Wrap });
        }
        _toolTip.PlacementTarget = this;
        _toolTip.Content = panel;
        _toolTip.IsOpen = true;
        e.Handled = true;
    }

    private static IHighlightingDefinition? LoadHighlighting()
    {
        using var stream = typeof(YamlEditor).Assembly.GetManifestResourceStream("AutoSettings.Agent.Yaml.xshd");
        if (stream is null)
            return null;
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    /// <summary>A completion list entry.</summary>
    private sealed class CompletionData(CompletionItem item) : ICompletionData
    {
        public ImageSource? Image => null;

        public string Text => item.Label;

        public object Content => item.Label;

        public object Description => item.Description;

        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
            textArea.Document.Replace(completionSegment, item.InsertText);
    }

    /// <summary>Draws wavy underlines under lines with problems.</summary>
    private sealed class IssueRenderer : IBackgroundRenderer
    {
        private static readonly Pen ErrorPen = CreatePen(Colors.Red);
        private static readonly Pen WarningPen = CreatePen(Colors.Orange);

        public List<ConfigIssue> Issues { get; set; } = [];

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            var document = textView.Document;
            if (document is null || !textView.VisualLinesValid)
                return;
            foreach (var issue in Issues)
            {
                var location = issue.Location!.Value;
                if (location.Line < 1 || location.Line > document.LineCount)
                    continue;
                var line = document.GetLineByNumber(location.Line);
                var start = Math.Min(line.Offset + Math.Max(0, location.Column - 1), line.EndOffset);
                if (start >= line.EndOffset)
                    start = line.Offset;
                var segment = new TextSegment { StartOffset = start, EndOffset = Math.Max(start + 1, line.EndOffset) };
                var pen = issue.Severity == IssueSeverity.Error ? ErrorPen : WarningPen;
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                    DrawWave(drawingContext, pen, rect.BottomLeft, rect.Right);
            }
        }

        private static void DrawWave(DrawingContext context, Pen pen, Point start, double right)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(start, false, false);
                var up = true;
                for (var x = start.X + 3; x <= right; x += 3)
                {
                    ctx.LineTo(new Point(x, start.Y + (up ? -2 : 0)), true, false);
                    up = !up;
                }
            }
            geometry.Freeze();
            context.DrawGeometry(null, pen, geometry);
        }

        private static Pen CreatePen(Color color)
        {
            var pen = new Pen(new SolidColorBrush(color), 1);
            pen.Freeze();
            return pen;
        }
    }
}
