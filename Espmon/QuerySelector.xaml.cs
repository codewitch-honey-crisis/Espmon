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

        // Snapshot from the last evaluation. Every read-only surface below serves from this;
        // only Refresh() ever calls Run() to update it.
        private List<HardwareInfoEntry> _results = new List<HardwareInfoEntry>();
        private bool _hasMoreResults;

        // Stable instance. SuggestBox.ItemsSource is x:Bind'd to MatchingPaths, so handing it a
        // brand-new collection on every poll tears down the open dropdown mid-typing. We mutate
        // this one in place instead, which means MatchingPaths never needs a change notification.
        private readonly ObservableCollection<string> _matchingPaths = new ObservableCollection<string>();

        public QuerySelector()
        {
            this.InitializeComponent();

            // Read the DP rather than hardcoding: XAML-set values are already applied by the time
            // InitializeComponent returns, and the old Loaded handler clobbered them with 1000.
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, Interval));
            _timer.Tick += _timer_Tick;

            _validationBrush = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];

            this.Loaded += (s, e) => { EnsureInnerTextBox(); UpdatePolling(); };
            this.Unloaded += (s, e) => _timer.Stop();
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

        // The poll. Refresh() handles its own failures, so no swallowing wrapper here.
        private void _timer_Tick(object? sender, object e) => Refresh();
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
        private bool _settingSelectedPathFromText = false; // Text -> SelectedPath (user picking a suggestion)
        public HardwareInfoExpression? Expression
        {
            get => (HardwareInfoExpression)GetValue(ExpressionProperty);
            set => SetValue(ExpressionProperty, value);
        }
        private static void OnExpressionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not QuerySelector control) return;

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

            // What we poll just changed. No-op while ValidatePattern is mid-parse; it calls
            // UpdatePolling itself once the expression has settled.
            control.UpdatePolling();
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

            if (d is not QuerySelector control) return;

            // Unsubscribe unconditionally: the old code only ran when the NEW value was a
            // SessionController, so clearing Session to null leaked the handler.
            if (e.OldValue is SessionController oldCtrl)
            {
                oldCtrl.PropertyChanged -= control.Session_PropertyChanged;
            }
            if (e.NewValue is SessionController newCtrl)
            {
                newCtrl.PropertyChanged += control.Session_PropertyChanged;
            }

            control.UpdatePolling();
        }

        private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == null || e.PropertyName.Equals("ScreenIndex", StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(Expression));
                // The screen changed underneath us, so the cached snapshot describes the old one.
                // Don't wait up to a full Interval to correct it.
                Refresh();
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
            if (d is not QuerySelector control) return;

            // We originated this from a suggestion pick; the text is already right. Without this,
            // AcceptSuggestion's SelectedPath = "" pushed empty back and blanked the box.
            if (control._settingSelectedPathFromText) return;

            if (e.NewValue is string newPath && control._pathPattern != newPath)
            {
                control.PathPattern = newPath;
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
                    Debug.WriteLine($"New path pattern is {value}");
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

        /// <summary>
        /// A path as typed is an exact address. As a SUGGESTION SOURCE it's a prefix — someone
        /// typing /cpu/te wants /cpu/temp offered, and a half-typed path addresses nothing on its
        /// own. So the dropdown, and only the dropdown, evaluates path + ".*" as a match.
        /// The preview deliberately does not go through here: it shows what you actually typed.
        /// </summary>
        private static HardwareInfoExpression ToPrefixMatch(HardwareInfoPathExpression path)
        {
            var text = path.Path;
            if (string.IsNullOrEmpty(text))
                return HardwareInfoMatchExpression.MatchAll;

            // Unescaped and unanchored, as the original was. A path carrying regex syntax throws
            // here; RefreshSuggestions' catch degrades that to an empty dropdown, not a crash.
            return new HardwareInfoMatchExpression(
                new Regex(text + ".*", RegexOptions.Singleline | RegexOptions.CultureInvariant));
        }

        /// <summary>The realtime preview: the expression exactly as typed.</summary>
        private IEnumerable<HardwareInfoEntry> Run()
        {
            if (Session == null) return Array.Empty<HardwareInfoEntry>();
            return Session.Parent.Evaluate(Expression ?? new HardwareInfoEmptyExpression());
        }

        /// <summary>The dropdown candidates: the prefix match. Empty unless a path is being typed.</summary>
        private IEnumerable<HardwareInfoEntry> RunSuggestions()
        {
            if (Session == null || Expression is not HardwareInfoPathExpression path)
                return Array.Empty<HardwareInfoEntry>();

            return Session.Parent.Evaluate(ToPrefixMatch(path));
        }

        /// <summary>The live snapshot. Not a copy — callers must not mutate it.</summary>
        public IList<HardwareInfoEntry> Matches => _results;

        /// <summary>
        /// Stable collection instance, mutated in place by SyncProjections. Never reassigned,
        /// so it needs no PropertyChanged and the suggestion dropdown survives a poll.
        /// </summary>
        public IList<string> MatchingPaths => _matchingPaths;

        private static string Describe(HardwareInfoEntry entry)
            => $"{entry.Path ?? "(n/a)"} => {FloatToString(entry.Value)}{entry.Unit}";

        private void SyncProjections(IList<HardwareInfoEntry> source)
        {
            // Touch only what actually changed. On a steady-state poll the paths are identical
            // and only the values move, so this rewrites strings in place rather than issuing a
            // Reset that would collapse the open dropdown once per Interval.
            for (int i = 0; i < source.Count; i++)
            {
                var text = Describe(source[i]);
                if (i < _matchingPaths.Count)
                {
                    if (!string.Equals(_matchingPaths[i], text, StringComparison.Ordinal))
                        _matchingPaths[i] = text;
                }
                else
                {
                    _matchingPaths.Add(text);
                }
            }
            while (_matchingPaths.Count > source.Count)
                _matchingPaths.RemoveAt(_matchingPaths.Count - 1);
        }

        /// <summary>
        /// One Evaluate per tick. The preview (expression exactly as typed) and the dropdown
        /// candidates (prefix match) used to be two separate calls because they ask different
        /// questions. They can share: a PathExpression matches exactly, and any path matches its
        /// own "path + .*" regex, so the prefix result set contains the preview's. We evaluate the
        /// wider query and sieve the preview back out of it. Any non-path expression has no
        /// dropdown at all, so that case is a single call either way.
        /// </summary>
        private void EvaluateOnce(out List<HardwareInfoEntry> preview, out List<HardwareInfoEntry> candidates)
        {
            preview = new List<HardwareInfoEntry>();
            candidates = new List<HardwareInfoEntry>();

            if (Session == null) return;

            if (Expression is not HardwareInfoPathExpression path)
            {
                // 51, so we can tell "exactly 50" from "50 and then some".
                preview = Session.Parent.Evaluate(Expression ?? new HardwareInfoEmptyExpression())
                                        .Take(51).ToList();
                return;
            }

            var exact = path.Path ?? string.Empty;

            // Single pass over one enumerable. Note we don't stop at 50: the dropdown caps there,
            // but the exact entry can sit past the cap and the preview still needs it.
            foreach (var entry in Session.Parent.Evaluate(ToPrefixMatch(path)))
            {
                if (candidates.Count < 50) candidates.Add(entry);

                if (preview.Count < 51 && string.Equals(entry.Path, exact, StringComparison.Ordinal))
                    preview.Add(entry);
            }
        }

        private void Refresh()
        {
            List<HardwareInfoEntry> fresh, candidates;
            try
            {
                // ToPrefixMatch throws on a path carrying regex syntax; that lands here now too,
                // which degrades to an empty preview + empty dropdown rather than a crash.
                EvaluateOnce(out fresh, out candidates);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Evaluate failed: {ex.Message}");
                fresh = new List<HardwareInfoEntry>();
                candidates = new List<HardwareInfoEntry>();
            }

            _hasMoreResults = fresh.Count > 50;
            if (_hasMoreResults) fresh.RemoveAt(50);

            _results = fresh;
            SyncProjections(candidates);

            OnPropertyChanged(nameof(MatchCountText));
            OnPropertyChanged(nameof(EvaluatedText));
        }
       

        /// <summary>
        /// Starts or stops the poll, and clears the preview when there's nothing to poll.
        /// Called from every place that can change the session, the expression, or validity.
        /// </summary>
        private void UpdatePolling()
        {
            // ValidatePattern reassigns Expression while parsing; let it settle and call this
            // itself when it's done, rather than thrashing the timer on an interim value.
            if (_settingExpressionFromText) return;

            var shouldPoll = Session != null
                && Expression != null
                && !Expression.IsEmpty
                && _validationException == null;

            if (shouldPoll)
            {
                // Guard the Start: DispatcherTimer.Start on a running timer restarts the
                // interval, so calling it per keystroke would starve the poll while typing.
                if (!_timer.IsEnabled) _timer.Start();
                Refresh();   // paint immediately; don't make the user wait a full Interval
            }
            else
            {
                _timer.Stop();
                if (_results.Count > 0)
                {
                    _results = new List<HardwareInfoEntry>();
                    _hasMoreResults = false;
                }
                SyncProjections(Array.Empty<HardwareInfoEntry>());
                OnPropertyChanged(nameof(MatchCountText));
                OnPropertyChanged(nameof(EvaluatedText));
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
                // A zero interval would spin the dispatcher evaluating hardware queries.
                control._timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, (int)e.NewValue));
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
            get
            {
                if (Session == null || Expression == null || Expression.IsEmpty) return "(no result)";
                // Min(50, Count) reported a truncated 200-match query as a flat "50". _hasMoreResults
                // comes from taking 51 and is the only thing that knows the difference.
                return _hasMoreResults ? "50+" : _results.Count.ToString();
            }
        }
        private static string FloatToString(float value)
        {
            if (float.IsNaN(value)) return "NaN";
            return Math.Round(value, 2).ToString("G");
        }
        // Reads the snapshot only; Refresh() already absorbed any evaluation failure.
        public string EvaluatedText
            => _results.Count > 0
                ? string.Join(", ", _results.Select(p => $"{FloatToString(p.Value)}{p.Unit}"))
                : "(no result)";
        public Visibility MatchVisibility
        {
            get
            {
                // Was IsQueryExpression only, from when a path resolved to a single sensor and a
                // count was meaningless. Paths produce real counts now, so they get the header.
                return (IsQueryExpression || IsRegexExpression || Expression is HardwareInfoPathExpression)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
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
                ValidationErrorMessage = null;   // was left showing the previous error after a clear
                _validationException = null;
                OnPropertyChanged(nameof(IsQueryExpression));
                OnPropertyChanged(nameof(IsRegexExpression));
                OnPropertyChanged(nameof(MatchVisibility));

                // The expression is gone: stop the poll and wipe the preview. The old early return
                // did neither, so the timer kept evaluating and the last result stayed on screen.
                UpdatePolling();
                return;
            }

            ValidatePattern();
        }

        private void ValidatePattern()
        {
            var args = new PatternValidationEventArgs { Pattern = _pathPattern };

            _settingExpressionFromText = true;   // suppress Expression -> Text while parsing user input
            try
            {
                // Removed: an interim `Expression = new HardwareInfoEmptyExpression()` here. It
                // fired a spurious DP change on every keystroke, which now means a stop/clear
                // followed immediately by a start/re-evaluate — visible flicker for no gain.

                HardwareInfoExpression? expr = null;
                _validationException = null;

                try
                {
                    expr = HardwareInfoExpression.Parse(_pathPattern);
                }
                catch (Exception ex)
                {
                    // Rebuilt; the original body was lost. Nothing else in this method ever set
                    // IsValid to false, so a parse failure used to fall through to the SUCCESS
                    // branch with a checkmark and a silently-emptied Expression.
                    _validationException = ex;
                    args.IsValid = false;
                    args.ErrorMessage = ex.Message;
                }

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
                    ValidationBrush = (Brush)Resources["ValidationErrorBrush"];
                    ValidationIcon = "\uE783";
                    if (!_syncingTextFromExpression) Expression = new HardwareInfoEmptyExpression();
                }
            }
            finally { _settingExpressionFromText = false; }

            OnPropertyChanged(nameof(IsRegexExpression));
            OnPropertyChanged(nameof(IsQueryExpression));
            OnPropertyChanged(nameof(MatchVisibility));   // was never notified; the header got stuck

            // Expression has settled. Start or stop the poll to match, and paint the first
            // preview now. Every expression type is live, so the type no longer gates the timer.
            UpdatePolling();
        }

        #endregion

        #region Event Handlers

        private void SuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // Only user typing should pop the list open. Programmatic text changes (canonicalizing
            // on LostFocus, an external Expression push) must stay silent.
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

            // Drive the update from the box's own text rather than trusting that the TwoWay x:Bind
            // on Text has already pushed into PathPattern — the ordering between that binding and
            // this event isn't guaranteed, and we need _matchingPaths current on the next line.
            // The setter no-ops if the binding did already run.
            PathPattern = sender.Text;

            // MatchingPaths is one stable instance now, so ItemsSource never changes identity and
            // AutoSuggestBox never decides on its own to open. Say so explicitly. This is the
            // trade for not tearing the dropdown down on every poll.
            sender.IsSuggestionListOpen = _matchingPaths.Count > 0;
        }

        /// <summary>
        /// Shared by SuggestionChosen and QuerySubmitted: strip the " => value" display suffix,
        /// put the bare path in the box, and mirror it to SelectedPath when it really is a path.
        /// </summary>
        private void AcceptSuggestion(string chosenPath)
        {
            var idx = chosenPath.IndexOf(" => ", StringComparison.Ordinal);
            if (idx > -1) { chosenPath = chosenPath.Substring(0, idx); }

            PathPattern = chosenPath;

            _settingSelectedPathFromText = true;
            try
            {
                // Was SelectedPath.StartsWith(...) — testing the value being REPLACED rather than
                // the one just picked, so the first pick from an empty SelectedPath always missed.
                SelectedPath = chosenPath.StartsWith("/", StringComparison.Ordinal) ? chosenPath : "";
            }
            finally { _settingSelectedPathFromText = false; }
        }

        private void SuggestBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string chosenPath)
            {
                AcceptSuggestion(chosenPath);
            }
        }

        private void SuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion is string chosenPath)
            {
                AcceptSuggestion(chosenPath);
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
        private MenuFlyoutHeader MakeHeader(string text)
        {
            return new MenuFlyoutHeader
            {
                Text = text,
                // Assigned explicitly by key, not implicitly: the flyout renders in a popup
                // outside this control's visual tree and would never resolve an implicit style.
                Style = (Style)Resources["MenuFlyoutHeaderStyle"]
            };
        }

        private MenuFlyoutItem MakeSuggestionItem(
    IHardwareInfoProvider provider,
    HardwareInfoSuggestionContext context,
    HardwareInfoSuggestion suggestion)
        {
            var item = new MenuFlyoutItem
            {
                Tag = suggestion.Key,
                Text = suggestion.Action
            };

            item.Click += (s, clickArgs) =>
            {
                var expr = provider.ApplySuggestion(context, item.Tag);
                SuggestBox.Text = expr?.ToString() ?? "";
                SuggestBox.Focus(FocusState.Programmatic);
            };

            return item;
        }
        /// <summary>
        /// The provider-independent transforms (round/avg/past/…). These come from the static
        /// HardwareInfoSuggestion, not from any provider — HardwareInfoProviderBase deliberately
        /// returns nothing, so routing them through a provider would just duplicate them per header.
        /// </summary>
        private MenuFlyoutItem? MakeExpressionItem(
            HardwareInfoSuggestionContext context,
            HardwareInfoSuggestion suggestion)
        {
            var item = new MenuFlyoutItem
            {
                Tag = suggestion.Key,
                Text = suggestion.Action
            };

            item.Click += (s, clickArgs) =>
            {
                var expr = HardwareInfoSuggestion.ApplySuggestion(context, item.Tag);
                if (expr == null) return;   // unrecognized key: leave their text alone, don't blank it
                SuggestBox.Text = expr.ToString();
                SuggestBox.Focus(FocusState.Programmatic);
            };

            return item;
        }

        /// <summary>
        /// An item that unions the provider's answer-for-a-blank-box onto the current expression
        /// instead of replacing it. Returns null when the suggestion doesn't survive the probe —
        /// see the ContainsEmpty note below.
        /// </summary>
        private MenuFlyoutItem? MakeUnionItem(
            IHardwareInfoProvider provider,
            HardwareInfoSuggestionContext probeContext,
            HardwareInfoSuggestion suggestion,
            HardwareInfoExpression current)
        {
            var addition = provider.ApplySuggestion(probeContext, suggestion.Key);

            // The probe's Expression is empty, so anything the provider built *around* it (avg(),
            // round(), past(30 sec, )) comes back with an empty node still in it. Those are the
            // generic base-class suggestions, not a self-contained query — drop them.
            if (addition == null || ContainsEmpty(addition)) return null;

            // Union is lowest-precedence and left-associative, and its ToString parenthesizes a
            // nested union on the right, so building it here needs no bracketing of our own.
            var text = new HardwareInfoUnionExpression(current.Clone(), addition).ToString();

            var item = new MenuFlyoutItem
            {
                Tag = suggestion.Key,
                Text = suggestion.Action
            };

            // Resolved at open time on purpose: the expression can't change while the flyout is up.
            item.Click += (s, clickArgs) =>
            {
                SuggestBox.Text = text;
                SuggestBox.Focus(FocusState.Programmatic);
            };

            return item;
        }

        private static bool ContainsEmpty(HardwareInfoExpression? expr) => expr switch
        {
            null => true,
            HardwareInfoEmptyExpression => true,
            HardwareInfoUnaryExpression u => ContainsEmpty(u.Expression),
            HardwareInfoBinaryExpression b => ContainsEmpty(b.Left) || ContainsEmpty(b.Right),
            HardwareInfoNAryExpression n => n.Children.Any(ContainsEmpty),
            _ => false
        };

        /// <summary>
        /// Uncategorized items first in provider order, then one submenu per category, alphabetical.
        /// The factory may return null to reject a suggestion; a category whose every member is
        /// rejected produces no submenu.
        /// </summary>
        private List<MenuFlyoutItemBase> BuildGroupItems(
            IEnumerable<HardwareInfoSuggestion> suggestions,
            Func<HardwareInfoSuggestion, MenuFlyoutItem?> factory)
        {
            var all = suggestions.ToList();
            var groupItems = new List<MenuFlyoutItemBase>();

            foreach (var suggestion in all.Where(s => string.IsNullOrWhiteSpace(s.Category)))
            {
                var item = factory(suggestion);
                if (item != null) groupItems.Add(item);
            }

            var categorized = all
                .Where(s => !string.IsNullOrWhiteSpace(s.Category))
                .GroupBy(s => s.Category!.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

            foreach (var categoryGroup in categorized)
            {
                var subMenu = new MenuFlyoutSubItem { Text = categoryGroup.Key };

                foreach (var suggestion in categoryGroup)
                {
                    var item = factory(suggestion);
                    if (item != null) subMenu.Items.Add(item);
                }

                if (subMenu.Items.Count == 0) continue;
                groupItems.Add(subMenu);
            }

            return groupItems;
        }

        /// <summary>Header + items, with a rule between groups but never above the first one.</summary>
        private void AddGroup(string? header, List<MenuFlyoutItemBase> groupItems)
        {
            if (groupItems.Count == 0) return;   // a provider that yields nothing leaves no dangling header

            if (ChevronFlyout.Items.Count > 0)
            {
                ChevronFlyout.Items.Add(new MenuFlyoutSeparator());
            }
            if (!string.IsNullOrEmpty(header))
            {
                ChevronFlyout.Items.Add(MakeHeader(header));
            }
            foreach (var item in groupItems)
            {
                ChevronFlyout.Items.Add(item);
            }
        }

        private void ChevronFlyout_Opening(object sender, object e)
        {
            ChevronFlyout.Items.Clear();

            if (Session != null && Matches != null && Session.Parent is LocalPortController controller)
            {
                var providers = controller.GetHardwareProviders();
                var matches = Matches.ToList();
                var parseException = ValidationException as HardwareInfoParseException;

                var context = new HardwareInfoSuggestionContext(Expression, matches, parseException, providers);

                var current = Expression;
                var hasExpression = current != null && !current.IsEmpty && parseException == null;

                // Normal pass: whatever the providers offer for the expression as it actually stands.
                // With a non-empty expression most of these come back empty (HardwareInfoProviderBase
                // returns nothing by default), which is fine — AddGroup drops headerless groups.
                foreach (var provider in providers)
                {
                    AddGroup(
                        provider.DisplayName,
                        BuildGroupItems(
                            provider.GetSuggestions(context),
                            s => MakeSuggestionItem(provider, context, s)));
                }

                // Expression-level transforms (round/avg/past/…). Provider-independent, so they get one
                // group of their own rather than being repeated under every provider header.
                //
                // Gated on a real expression: with an empty box FillSuggestions still offers "Take the
                // average", but ApplySuggestion would wrap the empty expression and hand back avg().
                if (hasExpression)
                {
                    AddGroup(
                        null,
                        BuildGroupItems(
                            HardwareInfoSuggestion.GetSuggestions(context),
                            s => MakeExpressionItem(context, s)));
                }

                // Union pass. Only meaningful when there's something to union WITH and it parsed —
                // otherwise the normal pass already surfaced these same root suggestions.
                if (hasExpression)
                {
                    // The lie that makes this work: providers key off IsEmpty, so an empty-expression
                    // context gets us their root suggestions verbatim. We keep the real matches and a
                    // null ParseException so nothing else in the provider reads as inconsistent.
                    var probe = new HardwareInfoSuggestionContext(
                        new HardwareInfoEmptyExpression(), matches, null, providers);

                    foreach (var provider in providers)
                    {
                        AddGroup(
                            $"Union w/ {provider.DisplayName}",
                            BuildGroupItems(
                                provider.GetSuggestions(probe),
                                s => MakeUnionItem(provider, probe, s, current!)));
                    }
                }
            }

            if (ChevronFlyout.Items.Count == 0)
            {
                ChevronFlyout.Items.Add(new MenuFlyoutItem { Text = "No suggestions available", IsEnabled = false });
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