using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HWKit;
using System.Diagnostics;
using System.Runtime.Versioning;
namespace Espmon
{
    [SupportedOSPlatform("windows")]
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
        // Snapshot from the last genuine evaluation. Every read-only surface below serves
        // from this; only RerunQuery() ever calls Run() to refresh it.
        private IList<HardwareInfoEntry> _results = Array.Empty<HardwareInfoEntry>();
        public QuerySelector()
        {
            this.InitializeComponent();
            _timer.Tick += _timer_Tick;
            //AvailablePaths = new ObservableCollection<string>();
            _validationBrush = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            ;
            // in the constructor, alongside this.Loaded:
            this.Loaded += (s, e) => { EnsureInnerTextBox(); _timer.Interval = TimeSpan.FromMilliseconds(1000); };
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
            // Cut/Copy/Paste/Select-All come from the TextBox's built-in context menu and shortcuts.
        }

        private void _timer_Tick(object? sender, object e)
        {
            try
            {
                RerunQuery();
            }
            catch { }
            OnPropertyChanged(nameof(EvaluatedText));
        }
        public Exception? ValidationException => _validationException;
        private static HardwareInfoEmptyExpression _emptyExpr = new HardwareInfoEmptyExpression();
        public static readonly DependencyProperty ExpressionProperty =
    DependencyProperty.Register(
        nameof(Expression),
        typeof(HardwareInfoExpression),
        typeof(QuerySelector),
        new PropertyMetadata(_emptyExpr, OnExpressionChanged));
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
                //control.RerunQuery();

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
                if (e.OldValue is SessionController oldCtrl)
                {
                    oldCtrl.PropertyChanged -= control.Session_PropertyChanged;
                }
                ctrl.PropertyChanged += control.Session_PropertyChanged;
                //control.RerunQuery();

            }
        }

        private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == null || e.PropertyName.Equals("ScreenIndex", StringComparison.Ordinal))
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
        public bool IsRegexExpression
        {
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
            else if (Expression != null)
            {
                return Session.Parent.Evaluate(Expression);
            }
            return Array.Empty<HardwareInfoEntry>();
        }
        public IList<HardwareInfoEntry>? Matches => _results.ToObservableList();

        //public IList<string>? MatchingPaths
        //{
        //    get
        //    {
        //        try
        //        {
        //            return _results
        //        .Select(p => $"{p.Path ?? "(n/a)"} => {FloatToString(p.Value)}{p.Unit}")
        //        .ToLazyList()
        //        .ToObservableList();
        //        }
        //        catch
        //        {
        //            return Array.Empty<string>().ToObservableList();
        //        }
        //    }
        //}
        public IList<string>? MatchingPaths
        {
            get
            {
                try
                {
                    return new ObservableCollection<string>(
                        _results.Select(p => $"{p.Path ?? "(n/a)"} => {FloatToString(p.Value)}{p.Unit}"));
                }
                catch
                {
                    return new ObservableCollection<string>();
                }
            }
        }
        public Brush ValidationBrush
        {
            get => _validationBrush;
            private set
            {
                _validationBrush = value;
                OnPropertyChanged(nameof(ValidationBrush));
            }
        }

        public static readonly DependencyProperty IntervalProperty =
    DependencyProperty.Register(
        nameof(Interval),
        typeof(int),
        typeof(QuerySelector),
        new PropertyMetadata(1000, OnIntervalChanged));
        public int Interval
        {
            get => (int)GetValue(IntervalProperty);
            set
            {
                SetValue(IntervalProperty, value);
            }
        }
        private static void OnIntervalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is QuerySelector control)
            {
                control._timer.Interval = TimeSpan.FromMilliseconds((int)e.NewValue);
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

        public string MatchCountText => _results.Count >= 50 ? "50+" : _results.Count.ToString();
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
                    return Session != null ? Expression != null && !Expression.IsEmpty ? string.Join(", ", Session.Parent.Evaluate(Expression).Select(p => $"{FloatToString(p.Value)}{p.Unit}")) : "(no result)" : "(no result)";
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
            // The one and only place Run() genuinely executes. Evaluate + cap + materialize
            // once, cache the snapshot, then notify. The getters above never call Run(),
            // so binding re-reads are free.
            try
            {
                _results = Run().Take(50).ToList();
            }
            catch
            {
                return;
            }
            try
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Matches)));
            }
            catch { }
            try
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MatchingPaths)));
            }
            catch { }
            try
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MatchCountText)));
            }
            catch { }
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
                //RerunQuery();
                return;
            }

            ValidatePattern();
            //    RerunQuery();
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
                }
                else
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
            var toSnapshot = this.MatchingPaths ?? [];

            // Clear existing items and populate from event args
            ChevronFlyout.Items.Clear();
            if (Session != null && Matches != null)
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
            if (ChevronFlyout.Items.Count == 0)
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