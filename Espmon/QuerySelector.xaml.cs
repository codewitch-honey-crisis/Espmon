using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HWKit;
using System.Diagnostics;
namespace Espmon
{
    public sealed partial class QuerySelector : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _pathPattern = string.Empty;
        private Brush _validationBrush;
        private string? _validationErrorMessage = null;
        private Exception? _validationException = null;
        private string _validationIcon = "\uE946"; // Search icon
        private TextBox? _innerTextBox;
        private DispatcherTimer _timer = new DispatcherTimer();
        private string _liveSelection = "";   // current selection text while it's non-empty
        private string _recentlyCleared = "";   // what was selected the instant before it emptied
        private long _recentlyClearedAt = 0;    // when that emptying happened (ms)

        public QuerySelector()
        {
            this.InitializeComponent();
            _timer.Tick += _timer_Tick;
            //AvailablePaths = new ObservableCollection<string>();
            _validationBrush = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            ;
            // in the constructor, alongside this.Loaded:
            this.Loaded += (s, e) => { EnsureInnerTextBox(); _timer.Interval = TimeSpan.FromMilliseconds(100); };
            SuggestBox.GotFocus += (s, e) => EnsureInnerTextBox();   // fallback if Loaded was too early
        }
        private void EnsureInnerTextBox()
        {
            if (_innerTextBox != null) return;

            _innerTextBox = FindDescendant<TextBox>(SuggestBox);
            if (_innerTextBox == null)
            {
                return;
            }

            _innerTextBox.IsSpellCheckEnabled = false;
            _innerTextBox.ContextFlyout = BuildClipboardFlyout();
            _innerTextBox.SelectionChanged += (s, e) =>
            {
                if (_innerTextBox.SelectionLength > 0)
                {
                    _liveSelection = _innerTextBox.SelectedText;
                }
                else if (_liveSelection.Length > 0)
                {
                    // selection just emptied — remember it briefly, with a timestamp,
                    // so a native cut that deleted it can still grab it this keystroke
                    _recentlyCleared = _liveSelection;
                    _recentlyClearedAt = Environment.TickCount64;
                    _liveSelection = "";
                }
            };

            _innerTextBox.KeyDown += InnerTextBox_KeyDown;       // one subscription, normal bubbling
          
        }
        private MenuFlyout BuildClipboardFlyout()
        {
            var flyout = new MenuFlyout();

            var cut = new MenuFlyoutItem { Text = "Cut", Icon = new SymbolIcon(Symbol.Cut) };
            var copy = new MenuFlyoutItem { Text = "Copy", Icon = new SymbolIcon(Symbol.Copy) };
            var paste = new MenuFlyoutItem { Text = "Paste", Icon = new SymbolIcon(Symbol.Paste) };
            var selectAll = new MenuFlyoutItem { Text = "Select All" };

            cut.Click += (s, e) => CutToClipboard();
            copy.Click += (s, e) => CopyToClipboard();
            paste.Click += (s, e) => PasteFromClipboard();
            selectAll.Click += (s, e) => _innerTextBox?.SelectAll();

            flyout.Items.Add(cut);
            flyout.Items.Add(copy);
            flyout.Items.Add(paste);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(selectAll);

            // Enable/disable based on selection + clipboard contents at open time
            flyout.Opening += (s, e) =>
            {
                bool hasSelection = (_innerTextBox?.SelectionLength ?? 0) > 0;
                cut.IsEnabled = hasSelection;
                copy.IsEnabled = hasSelection;
                paste.IsEnabled = Win32Clipboard.HasText();
            };

            return flyout;
        }
        private void CopyToClipboard()
        {
            if (_innerTextBox == null || _innerTextBox.SelectionLength == 0)
            {
                Debug.WriteLine($"[copy] skipped: tb={_innerTextBox != null}, sel={_innerTextBox?.SelectionLength}");
                return;
            }
            bool ok = Win32Clipboard.SetText(_innerTextBox.SelectedText);
            Debug.WriteLine($"[copy] SetText('{_innerTextBox.SelectedText}') -> {ok}");
        }
        private void CutToClipboard()
        {
            if (_innerTextBox == null || _innerTextBox.SelectionLength == 0) return;
            if (Win32Clipboard.SetText(_innerTextBox.SelectedText))
                ReplaceSelection(string.Empty);
        }

        private void PasteFromClipboard()
        {
            if (_innerTextBox == null) return;
            var text = Win32Clipboard.GetText();
            if (string.IsNullOrEmpty(text)) return;
            ReplaceSelection(text);
        }

        private void ReplaceSelection(string replacement)
        {
            if (_innerTextBox == null) return;
            Debug.WriteLine($"[replace] '{replacement}' caret={_innerTextBox.SelectionStart}");
            var current = _innerTextBox.Text ?? string.Empty;
            int start = _innerTextBox.SelectionStart;
            int len = _innerTextBox.SelectionLength;

            // clamp defensively in case selection got stale
            start = Math.Clamp(start, 0, current.Length);
            len = Math.Clamp(len, 0, current.Length - start);

            _innerTextBox.Text = current.Substring(0, start) + replacement + current.Substring(start + len);
            _innerTextBox.SelectionStart = start + replacement.Length;
            _innerTextBox.SelectionLength = 0;
            _innerTextBox.Focus(FocusState.Programmatic);
        }

        private void InnerTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            bool ctrl = Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (!ctrl || _innerTextBox == null) return;

            switch (e.Key)
            {
                case Windows.System.VirtualKey.C:
                    Debug.WriteLine($"[kb] copy sel='{_innerTextBox.SelectedText}'");
                    if (_innerTextBox.SelectionLength > 0)
                        Win32Clipboard.SetText(_innerTextBox.SelectedText);
                    break;

                //case Windows.System.VirtualKey.X:
                //    // native already deleted the selection by now, so use the cache
                //    var text = _innerTextBox.SelectionLength > 0
                //        ? _innerTextBox.SelectedText
                //        : _lastNonEmptySelection;
                //    Debug.WriteLine($"[kb] cut text='{text}'");
                //    if (!string.IsNullOrEmpty(text))
                //        Win32Clipboard.SetText(text);
                //    break;
                case Windows.System.VirtualKey.X:
                    {
                        string text;
                        if (_innerTextBox.SelectionLength > 0)
                            text = _innerTextBox.SelectedText;                      // live selection (handler beat native)
                        else if (Environment.TickCount64 - _recentlyClearedAt < 100)
                            text = _recentlyCleared;                                // native cut emptied it just now
                        else
                            text = "";                                              // nothing was really selected — skip
                        if (!string.IsNullOrEmpty(text))
                            Win32Clipboard.SetText(text);
                        break;
                    }
                    // V and A intentionally NOT handled — native paste & select-all work fine.
            }
        }
        private void _timer_Tick(object? sender, object e)
        {
            RerunQuery();
            OnPropertyChanged(nameof(EvaluatedText));
        }
        public Exception? ValidationException => _validationException;
        private static HardwareInfoEmptyExpression _emptyExpr = new HardwareInfoEmptyExpression();
        public static readonly DependencyProperty ExpressionProperty =
    DependencyProperty.Register(
        nameof(Expression),
        typeof(HardwareInfoExpression),
        typeof(QuerySelector),
        new PropertyMetadata(_emptyExpr,OnExpressionChanged));
        // replace: private bool _updatingExpression = false;
        private bool _syncingTextFromExpression = false; // Expression -> Text (external change)
        private bool _settingExpressionFromText = false; // Text -> Expression (user typing)
        public HardwareInfoExpression? Expression
        {
            get => (HardwareInfoExpression)GetValue(ExpressionProperty);
            set => SetValue(ExpressionProperty, value);
        }
        private static void OnExpressionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is QuerySelector control)
            {
                control.RerunQuery();

                // Push canonical text into the box ONLY on external changes (screen/session
                // switch). Never while the user is typing — that's what blanked the box.
                if (!control._settingExpressionFromText)
                {
                    var newText = (e.NewValue as HardwareInfoExpression)?.ToString() ?? "";
                    if (control._pathPattern != newText)
                    {
                        control._syncingTextFromExpression = true;
                        control.PathPattern = newText;
                        control._syncingTextFromExpression = false;
                    }
                }
            }
        }
        public static readonly DependencyProperty SessionProperty =
            DependencyProperty.Register(
                nameof(Session),
                typeof(SessionController),
                typeof(QuerySelector),
                new PropertyMetadata(null, OnSessionChanged));

        public SessionController? Session
        {
            get => (SessionController)GetValue(SessionProperty);
            set => SetValue(SessionProperty, value);
        }

        private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            
            if (d is QuerySelector control && e.NewValue is SessionController ctrl)
            {
                if(e.OldValue is SessionController oldCtrl)
                {
                    oldCtrl.PropertyChanged -= control.Session_PropertyChanged;
                }
                ctrl.PropertyChanged += control.Session_PropertyChanged;
                control.RerunQuery();

            }
        }

        private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if(e.PropertyName==null||e.PropertyName.Equals("ScreenIndex",StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(Expression));
            }
        }

        public static readonly DependencyProperty SelectedPathProperty =
            DependencyProperty.Register(
                nameof(SelectedPath),
                typeof(string),
                typeof(QuerySelector),
                new PropertyMetadata(string.Empty, OnSelectedPathChanged));

        public string SelectedPath
        {
            get => (string)GetValue(SelectedPathProperty);
            set => SetValue(SelectedPathProperty, value);
        }

        private static void OnSelectedPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is QuerySelector control && e.NewValue is string newPath)
            {
                if (control._pathPattern != newPath)
                {
                    control.PathPattern = newPath;
                }
            }
        }


       
        private T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T result)
                    return result;

                var descendant = FindDescendant<T>(child);
                if (descendant != null)
                    return descendant;
            }

            return null;
        }

     
        public string PathPattern
        {
            get => _pathPattern;
            set
            {
                if (_pathPattern != value)
                {

                    _pathPattern = value;
                    OnPropertyChanged(nameof(PathPattern));
                    ValidateAndUpdateMatches();

                }
            }
        }
        public bool IsRegexExpression {
            get
            {
                return Expression is HardwareInfoMatchExpression;
            }
        }
        public bool IsQueryExpression
        {
            get
            {
                var expr = Expression;
                return expr == null || (expr is HardwareInfoQueryExpression);
            }
        }
      
        private IEnumerable<HardwareInfoEntry> Run()
        {
            if (Session == null) return Array.Empty<HardwareInfoEntry>();
            HardwareInfoExpression? expr = null;
            if (Expression == null)
            {
                expr = HardwareInfoMatchExpression.MatchAll;
            }
            if (Expression is HardwareInfoPathExpression path)
            {
                try
                {
                    expr = new HardwareInfoMatchExpression(new Regex(path.Path ?? "/", RegexOptions.Singleline | RegexOptions.CultureInvariant));
                }
                catch
                {
                    // TODO: Display the error
                }
            }
            else if (Expression is HardwareInfoQueryExpression query)
            {
                expr = query;
            }
            if (expr != null)
            {
                try
                {
                    return Session.Parent.Evaluate(expr);
                }
                catch
                {
                    // TODO: Display the error
                }
            }
            return Array.Empty<HardwareInfoEntry>();
        }
        public IList<HardwareInfoEntry>? Matches => Run().Take(50).ToLazyList().ToObservableList();
        public IList<string>? MatchingPaths => Run().Take(50).Select(p =>$"{p.Path??"(n/a)"} => {FloatToString(p.Value)}{p.Unit}").ToLazyList().ToObservableList();

        public Brush ValidationBrush
        {
            get => _validationBrush;
            private set
            {
                _validationBrush = value;
                OnPropertyChanged(nameof(ValidationBrush));
            }
        }
        public string? ValidationErrorMessage
        {
            get => _validationErrorMessage;
            private set
            {
                _validationErrorMessage = value;
                OnPropertyChanged(nameof(ValidationErrorMessage));
            }
        }

        public string ValidationIcon
        {
            get => _validationIcon;
            private set
            {
                if (_validationIcon != value)
                {
                    _validationIcon = value;
                    OnPropertyChanged(nameof(ValidationIcon));
                }
            }
        }

        public string MatchCountText
        {
            get => Matches==null?"n/a":Matches.Count>=50?"50+":Matches.Count.ToString();
        }
        private static string FloatToString(float value)
        {
            if (float.IsNaN(value)) return "NaN";
            return Math.Round(value, 2).ToString("G");
        }
        public string EvaluatedText
        {
            get
            {
                try
                {
                    return Session != null ? Expression!=null && !Expression.IsEmpty? string.Join(", ", Session.Parent.Evaluate(Expression).Select(p => $"{FloatToString(p.Value)}{p.Unit}")) : "(no result)" : "(no result)";
                }
                catch (Exception e)
                {
                    return e.Message;
                }
            }
        }
        public Visibility MatchVisibility
        {
            get
            {
                return IsQueryExpression ? Visibility.Visible : Visibility.Collapsed;
            }
        }
       
        void RerunQuery()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MatchingPaths)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MatchCountText)));
        }
        #region Validation and Matching

        private void ValidateAndUpdateMatches()
        {
            if (string.IsNullOrWhiteSpace(_pathPattern))
            {
                if (!_syncingTextFromExpression)
                {
                    _settingExpressionFromText = true;
                    Expression = null;
                    _settingExpressionFromText = false;
                }
                ValidationBrush = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
                ValidationIcon = "\uE946"; // Search
                _validationException = null;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQueryExpression)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRegexExpression)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MatchVisibility)));
                RerunQuery();
                return;
            }

            ValidatePattern();
            RerunQuery();
        }

        private void ValidatePattern()
        {
            var args = new PatternValidationEventArgs { Pattern = _pathPattern };

            _settingExpressionFromText = true;   // suppress Expression -> Text while parsing user input
            try
            {
                if (!_syncingTextFromExpression) Expression = new HardwareInfoEmptyExpression();

                HardwareInfoExpression? expr = new HardwareInfoEmptyExpression();
                _validationException = null;
                try
                {
                    expr = HardwareInfoExpression.Parse(_pathPattern);
                    if (!(expr is HardwareInfoQueryExpression)) _timer.Start(); else _timer.Stop();
                }
                catch { /* ...unchanged... */ }

                if (args.IsValid)
                {
                    ValidationErrorMessage = null;
                    ValidationBrush = (Brush)Resources["ValidationSuccessBrush"];
                    ValidationIcon = "\uE73E";
                    if (!_syncingTextFromExpression) Expression = expr;
                }
                else
                {
                    ValidationErrorMessage = args.ErrorMessage;
                    if (!_syncingTextFromExpression) Expression = new HardwareInfoEmptyExpression(); ;
                    ValidationBrush = (Brush)Resources["ValidationErrorBrush"];
                    ValidationIcon = "\uE783";
                }
            }
            finally { _settingExpressionFromText = false; }

            OnPropertyChanged(nameof(IsRegexExpression));
            OnPropertyChanged(nameof(IsQueryExpression));
            OnPropertyChanged(nameof(EvaluatedText));
        }

        #endregion

        #region Event Handlers

        private void SuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                //var textBox = FindTextBoxInAutoSuggestBox(sender);
                
            }
        }

        private void SuggestBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string chosenPath)
            {
                var idx = chosenPath.IndexOf(" => ");
                if (idx > -1) { chosenPath = chosenPath.Substring(0, idx); }

                PathPattern = chosenPath;
                // For plain paths, accept the selection
                if (SelectedPath.StartsWith("/"))
                {
                    SelectedPath = chosenPath;
                } else
                {
                    SelectedPath = "";
                }
            }
            // For other patterns, keep the query in the textbox
        }

        private void SuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion is string chosenPath)
            {
                var idx = chosenPath.IndexOf(" => ");
                if (idx > -1) { chosenPath = chosenPath.Substring(0, idx); }

                PathPattern = chosenPath;
                if (SelectedPath.StartsWith("/"))
                {
                    SelectedPath = chosenPath;
                }
            }
        }
        private void SuggestBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var canonical = Expression?.ToString();
            if (canonical == null) return;                                          // invalid/empty: leave their text alone
            if (string.Equals(_pathPattern, canonical, StringComparison.Ordinal))   // already canonical: don't touch
                return;

            _syncingTextFromExpression = true;
            PathPattern = canonical;
            _syncingTextFromExpression = false;
        }
        private void ChevronFlyout_Opening(object sender, object e)
        {
            var toSnapshot = this.MatchingPaths??[];
                        
            // Clear existing items and populate from event args
            ChevronFlyout.Items.Clear();
            if(Session!=null && Matches!=null)
            {
                //var providers = Session.Parent.GetProviders();
                //var context = new HardwareInfoSuggestionContext(Expression, Matches.ToList(), ValidationException as HardwareInfoParseException, providers);
                //foreach (var provider in providers)
                //{
                //    foreach(var suggestion in provider.GetSuggestions(context))
                //    {
                //        var item = new MenuFlyoutItem();
                //        item.Tag = suggestion.Key;
                //        item.Text = suggestion.Action;
                //        ChevronFlyout.Items.Add(item);
                //        item.Click += (s, clickArgs) =>
                //        {
                //            var expr = provider.ApplySuggestion(context, item.Tag);
                //            if(expr!=null)
                //            {
                //                SuggestBox.Text = expr.ToString();
                //            } else
                //            {
                //                SuggestBox.Text = "";
                //            }
                //            SuggestBox.Focus(FocusState.Programmatic);

                //        };

                //    }
                //}
            }
            if (ChevronFlyout.Items.Count== 0)
            {
                var emptyItem = new MenuFlyoutItem { Text = "No suggessions available", IsEnabled = false };
                ChevronFlyout.Items.Add(emptyItem);
            }
            
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

    }

    public class PatternValidationEventArgs : EventArgs
    {
        public string? Pattern { get; set; }
        public bool IsRegex { get; set; }
        public bool IsValid { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }

  
}