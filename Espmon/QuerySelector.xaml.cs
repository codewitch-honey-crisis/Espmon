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
        private DispatcherTimer _timer = new DispatcherTimer();


        public QuerySelector()
        {
            this.InitializeComponent();
            _timer.Tick += _timer_Tick;
            //AvailablePaths = new ObservableCollection<string>();
            _validationBrush = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            this.Loaded += (s, e) =>
            {
                var textBox = FindDescendant<TextBox>(SuggestBox);
                if (textBox != null)
                {
                    textBox.IsSpellCheckEnabled = false;
                }
                _timer.Interval = TimeSpan.FromMilliseconds(100);
            };
        }

        private void _timer_Tick(object? sender, object e)
        {
            RerunQuery();
            OnPropertyChanged(nameof(EvaluatedText));
        }
        public Exception? ValidationException => _validationException;
        #region Dependency Properties
        public static readonly DependencyProperty ExpressionProperty =
    DependencyProperty.Register(
        nameof(Expression),
        typeof(HardwareInfoExpression),
        typeof(QuerySelector),
        new PropertyMetadata(null,OnExpressionChanged));
        private bool _updatingExpression = false;
        public HardwareInfoExpression? Expression
        {
            get => (HardwareInfoExpression)GetValue(ExpressionProperty);
            set => SetValue(ExpressionProperty, value);
        }
        private static void OnExpressionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is QuerySelector control && e.NewValue is HardwareInfoExpression expr)
            {
                
                control.RerunQuery();
                control._updatingExpression = true;
                control.PathPattern = e.NewValue?.ToString()??"";
                control._updatingExpression = false;
            }
        }
        public static readonly DependencyProperty HardwareInfoProperty =
            DependencyProperty.Register(
                nameof(HardwareInfo),
                typeof(HardwareInfoCollection),
                typeof(QuerySelector),
                new PropertyMetadata(null, OnHardwareInfoChanged));

        public HardwareInfoCollection? HardwareInfo
        {
            get => (HardwareInfoCollection)GetValue(HardwareInfoProperty);
            set => SetValue(HardwareInfoProperty, value);
        }

        private static void OnHardwareInfoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is QuerySelector control && e.NewValue is HardwareInfoCollection hwInfo)
            {

                control.RerunQuery();

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

        #endregion

       
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

        #region Bindable Properties

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
            if (HardwareInfo == null) return Array.Empty<HardwareInfoEntry>();
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
                    return expr.Evaluate(HardwareInfo);
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
                    return HardwareInfo != null ? Expression != null ? string.Join(", ", Expression.Evaluate(HardwareInfo).Select(p => $"{FloatToString(p.Value)}{p.Unit}")) : "(no result)" : "(no result)";
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
        #endregion
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
                if (!_updatingExpression)
                {
                    Expression = null;
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
            var args = new PatternValidationEventArgs
            {
                Pattern = _pathPattern,
            };
            if (!_updatingExpression)
            {
                Expression = null;
            }
            HardwareInfoExpression? expr = null;
            _validationException = null;
            try
            {
                expr = HardwareInfoExpression.Parse(_pathPattern);
                if (!(expr is HardwareInfoQueryExpression))
                {
                    _timer.Start();
                } else
                {
                    _timer.Stop();

                }
            }
            catch (HardwareInfoParseException pe)
            {
                _timer.Stop();
                _validationException = pe;
                args.IsValid = false;
                args.ErrorMessage = $"{pe.Message} at {pe.Location.Position + 1}";
                ValidationErrorMessage = args.ErrorMessage;
                ValidationBrush = (Brush)Resources["ValidationErrorBrush"];
                ValidationIcon = "\uE783"; // Error
            }
            catch (Exception ex)
            {
                _timer.Stop();
                _validationException = ex;
                args.IsValid = false;
                args.ErrorMessage = ex.Message;
                ValidationErrorMessage = args.ErrorMessage;
                ValidationBrush = (Brush)Resources["ValidationErrorBrush"];
                ValidationIcon = "\uE783"; // Error
            }

            if (args.IsValid)
            {
                ValidationErrorMessage = null;
                ValidationBrush = (Brush)Resources["ValidationSuccessBrush"];
                ValidationIcon = "\uE73E"; // CheckMark
                if (!_updatingExpression)
                {
                    Expression = expr;
                }
            }
            else
            {
                ValidationErrorMessage = args.ErrorMessage;
                if (!_updatingExpression)
                {
                    Expression = null;
                }
                ValidationBrush = (Brush)Resources["ValidationErrorBrush"];
                ValidationIcon = "\uE783"; // Error
            }
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

        private void ChevronFlyout_Opening(object sender, object e)
        {
            var toSnapshot = this.MatchingPaths??[];
                        
            // Clear existing items and populate from event args
            ChevronFlyout.Items.Clear();
            if(HardwareInfo!=null && Matches!=null)
            {
                var context = new HardwareInfoSuggestionContext(Expression, Matches.ToList(), ValidationException as HardwareInfoParseException, HardwareInfo.Providers.ToArray());
                foreach (var provider in HardwareInfo.Providers)
                {
                    foreach(var suggestion in provider.GetSuggestions(context))
                    {
                        var item = new MenuFlyoutItem();
                        item.Tag = suggestion.Key;
                        item.Text = suggestion.Action;
                        ChevronFlyout.Items.Add(item);
                        item.Click += (s, clickArgs) =>
                        {
                            var expr = provider.ApplySuggestion(context, item.Tag);
                            if(expr!=null)
                            {
                                SuggestBox.Text = expr.ToString();
                            } else
                            {
                                SuggestBox.Text = "";
                            }
                            SuggestBox.Focus(FocusState.Programmatic);

                        };

                    }
                }
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