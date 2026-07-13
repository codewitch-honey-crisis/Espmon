using Microsoft.UI;                 // Colors
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;      // Brush, SolidColorBrush

using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Espmon
{
    /// <summary>
    /// A <see cref="TextBox"/> that only accepts characters legal in a Windows file <em>name</em>
    /// (not a path). Illegal characters are stripped as they are typed or pasted.
    ///
    /// Full validation (illegal chars, reserved device names, empty) is enforced when the control
    /// loses focus: if <see cref="EnforceValidOnLosingFocus"/> is true, the pending focus change is
    /// cancelled via <see cref="LosingFocusEventArgs.TryCancel"/> until the name is valid.
    ///
    /// LIMITATIONS of focus enforcement:
    ///  - TryCancel is best-effort and only reliably holds focus for in-app focus moves
    ///    (tab, clicking another control). It CANNOT prevent the user from switching windows/apps,
    ///    Alt+Tab, deactivating, or closing the window.
    ///  - Trapping focus on an empty field is a known UX/accessibility rough edge. Set
    ///    EnforceValidOnLosingFocus = false to disable, and prefer gating a "Save" button instead.
    /// </summary>
    /// <summary>Live validity state for a file name, suitable for driving an inline icon.</summary>
    public enum FileNameValidity
    {
        /// <summary>No input yet — show no icon.</summary>
        Empty,
        /// <summary>Valid Windows file name — show a green check.</summary>
        Valid,
        /// <summary>Invalid (illegal chars / reserved name / whitespace) — show a red X.</summary>
        Invalid,
    }

    public partial class FileNameTextBox : TextBox
    {
        /// <summary>
        /// Raised to validate the current (non-empty) text. Handlers set e.Cancel = true to mark the
        /// name INVALID. On entry e.Cancel is preset from the built-in Windows file-name check, so a
        /// handler can add rules (e.g. duplicate name -> Cancel = true) or relax it. The result drives
        /// the icon; on focus loss an invalid result cancels the focus change.
        /// Do NOT call Focus()/TryMoveFocus() inside a handler — moving focus during LosingFocus throws.
        /// </summary>
        public event EventHandler<CancelEventArgs>? Validating;
        // Path.GetInvalidFileNameChars(): control chars 0x00-0x1F plus  "  <  >  |  :  *  ?  \  /
        private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

        // Reserved / disallowed base names, matched (case-insensitively) against the base name
        // before the first '.'.
        //
        // NOTE: this is intentionally STRICTER than the Win32 file-naming spec, by design.
        // Windows only reserves COM1-COM9 / LPT1-LPT9 (plus superscript ¹ ² ³) as file names —
        // COM23, COM100, etc. are technically legal file names. But this app enumerates real
        // ports such as COM23, and a file name must NEVER collide with any COM#/LPT#, so we
        // reject COM/LPT followed by any run of digits. This is a deliberate product rule, not
        // the OS rule — do not "relax" it back to [1-9] without understanding that trade-off.
        // add (note: no Compiled — it's implied; keep IgnoreCase + CultureInvariant):
        [GeneratedRegex(
            @"^(?:CON|PRN|AUX|NUL|(?:COM|LPT)(?:[0-9]+|[\u00B9\u00B2\u00B3]))$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ReservedNameRegex();
        public FileNameTextBox()
        {
            // Make the derived type pick up the standard TextBox template/style. Optional if your
            // project already themes derived TextBoxes; if the control renders blank, this fixes it.
            DefaultStyleKey = typeof(TextBox);

            TextChanging += OnTextChanging;   // strip illegal chars (typed + pasted)
            TextChanged += OnTextChanged;    // recompute validity
            LosingFocus += OnLosingFocus;    // enforce full validity before focus leaves

            // in the constructor, replace UpdateValidity(); with:
            RunValidation();
        }

        public static readonly DependencyProperty IsFileNameValidProperty =
            DependencyProperty.Register(nameof(IsFileNameValid), typeof(bool),
                typeof(FileNameTextBox), new PropertyMetadata(false));

        /// <summary>
        /// True when the current text is a non-empty, non-reserved, valid Windows file name.
        /// Recalculated after every edit. Bind to it to drive an error visual or a Save button.
        /// </summary>
        public bool IsFileNameValid
        {
            get => (bool)GetValue(IsFileNameValidProperty);
            private set => SetValue(IsFileNameValidProperty, value);
        }

        public static readonly DependencyProperty ValidityProperty =
            DependencyProperty.Register(nameof(Validity), typeof(FileNameValidity),
                typeof(FileNameTextBox), new PropertyMetadata(FileNameValidity.Empty));

        /// <summary>
        /// Three-state validity for driving an inline icon: Empty (no icon), Valid (check),
        /// Invalid (X). Updated on every keystroke.
        /// </summary>
        public FileNameValidity Validity
        {
            get => (FileNameValidity)GetValue(ValidityProperty);
            private set => SetValue(ValidityProperty, value);
        }

        // Segoe Fluent Icons glyphs. Change these two if you prefer different symbols.
        private const string CheckGlyph = "\uE73E"; // CheckMark
        private const string CrossGlyph = "\uE711"; // Cancel (X)

        // Brushes for the icon. Swap for theme brushes (SystemFillColorSuccessBrush /
        // SystemFillColorCriticalBrush) if you want automatic light/dark + high-contrast.
        private static readonly Brush ValidBrush = new SolidColorBrush(Colors.Green);
        private static readonly Brush InvalidBrush = new SolidColorBrush(Colors.Red);
        private static readonly Brush EmptyBrush = new SolidColorBrush(Colors.Transparent);

        public static readonly DependencyProperty ValidityGlyphProperty =
            DependencyProperty.Register(nameof(ValidityGlyph), typeof(string),
                typeof(FileNameTextBox), new PropertyMetadata(string.Empty));

        /// <summary>Icon glyph for the current <see cref="Validity"/> (empty string when Empty).
        /// Bind a FontIcon's Glyph to this — no converter needed.</summary>
        public string ValidityGlyph
        {
            get => (string)GetValue(ValidityGlyphProperty);
            private set => SetValue(ValidityGlyphProperty, value);
        }

        public static readonly DependencyProperty ValidityBrushProperty =
            DependencyProperty.Register(nameof(ValidityBrush), typeof(Brush),
                typeof(FileNameTextBox), new PropertyMetadata(null));

        /// <summary>Icon color for the current <see cref="Validity"/> (transparent when Empty).
        /// Bind a FontIcon's Foreground to this — no converter needed.</summary>
        public Brush ValidityBrush
        {
            get => (Brush)GetValue(ValidityBrushProperty);
            private set => SetValue(ValidityBrushProperty, value);
        }

        public static readonly DependencyProperty BasePathProperty =
            DependencyProperty.Register(nameof(BasePath), typeof(string),
                typeof(FileNameTextBox), new PropertyMetadata(""));
        /// <summary>
        /// When true (default), focus is held in the control until <see cref="IsFileNameValid"/>
        /// is true (subject to the limitations noted on the class).
        /// </summary>
        public string BasePath
        {
            get => (string)GetValue(BasePathProperty);
            set => SetValue(BasePathProperty, value);
        }
        public static readonly DependencyProperty FileMustNotExistProperty =
            DependencyProperty.Register(nameof(FileMustNotExist), typeof(bool),
                typeof(FileNameTextBox), new PropertyMetadata(false));

        /// <summary>
        /// When true (default), focus is held in the control until <see cref="IsFileNameValid"/>
        /// is true (subject to the limitations noted on the class).
        /// </summary>
        public bool FileMustNotExist
        {
            get => (bool)GetValue(FileMustNotExistProperty);
            set => SetValue(EnforceValidOnLosingFocusProperty, value);
        }
        
        public static readonly DependencyProperty EnforceValidOnLosingFocusProperty =
            DependencyProperty.Register(nameof(EnforceValidOnLosingFocus), typeof(bool),
                typeof(FileNameTextBox), new PropertyMetadata(true));

        /// <summary>
        /// When true (default), focus is held in the control until <see cref="IsFileNameValid"/>
        /// is true (subject to the limitations noted on the class).
        /// </summary>
        public bool EnforceValidOnLosingFocus
        {
            get => (bool)GetValue(EnforceValidOnLosingFocusProperty);
            set => SetValue(EnforceValidOnLosingFocusProperty, value);
        }

        // ----- character filtering (typed + pasted) -----
        private void OnTextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            // This handler must limit itself to inspecting/updating Text and selection.
            string current = Text;
            if (!ContainsInvalidChar(current)) return;

            int caret = SelectionStart;
            int removedBeforeCaret = 0;

            var sb = new StringBuilder(current.Length);
            for (int i = 0; i < current.Length; i++)
            {
                char c = current[i];
                if (Array.IndexOf(InvalidChars, c) >= 0)
                {
                    if (i < caret) removedBeforeCaret++;
                }
                else
                {
                    sb.Append(c);
                }
            }

            // Reassigning re-raises TextChanging, but the cleaned string is clean, so it returns early.
            Text = sb.ToString();
            SelectionStart = Math.Max(0, caret - removedBeforeCaret);
        }

        // keystroke path: update the icon ONLY. No Validating event here.
        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            bool invalid = !IsValidWindowsFileName(Text);
            IsFileNameValid = !invalid;
            ApplyState(Text.Length == 0 ? FileNameValidity.Empty
                                        : invalid ? FileNameValidity.Invalid
                                                  : FileNameValidity.Valid);
        }

        // focus-loss path: THIS is where Validating fires, WinForms-style.
        private void OnLosingFocus(UIElement sender, LosingFocusEventArgs args)
        {
            var e = new CancelEventArgs { Cancel = !IsValidWindowsFileName(Text) };
            Validating?.Invoke(this, e);          // once, on the way out — like WinForms

            bool invalid = e.Cancel;
            IsFileNameValid = !invalid;
            ApplyState(Text.Length == 0 ? FileNameValidity.Empty
                                        : invalid ? FileNameValidity.Invalid
                                                  : FileNameValidity.Valid);

            if (!EnforceValidOnLosingFocus || !invalid) return;

            bool leavingToOtherWindow = args.NewFocusedElement is UIElement u && u.XamlRoot != XamlRoot;
            if (leavingToOtherWindow || Visibility != Visibility.Visible) return;

            if (args.TryCancel())
            {
                args.Handled = true;
                OnInvalidFocusLossPrevented();
            }
        }

        /// <summary>
        /// Called after focus loss was cancelled because the name is invalid. Override to surface
        /// an error message. Avoid adding/removing visual-tree elements from inside this synchronous
        /// focus event; prefer toggling an existing error TextBlock, setting a VisualState, or
        /// queuing UI work via DispatcherQueue.
        /// </summary>
        protected virtual void OnInvalidFocusLossPrevented()
        {
        }

        // ----- helpers -----
        private bool RunValidation()
        {
           
            var e = new CancelEventArgs { Cancel = !IsValidWindowsFileName(Text) };
            Validating?.Invoke(this, e);          // let the consumer add/override rules

            bool invalid = e.Cancel;
            IsFileNameValid = !invalid;
            ApplyState(invalid ? FileNameValidity.Invalid : FileNameValidity.Valid);
            return invalid;
        }

        private void ApplyState(FileNameValidity state)
        {
            Validity = state;
            ValidityGlyph = state switch
            {
                FileNameValidity.Valid => CheckGlyph,
                FileNameValidity.Invalid => CrossGlyph,
                _ => string.Empty,
            };
            ValidityBrush = state switch
            {
                FileNameValidity.Valid => ValidBrush,
                FileNameValidity.Invalid => InvalidBrush,
                _ => EmptyBrush,
            };
        }
        private static bool ContainsInvalidChar(string s)
        {
            foreach (char c in s)
                if (Array.IndexOf(InvalidChars, c) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Non-empty, no illegal characters, not a reserved device name. Windows also ignores a
        /// trailing dot/space, and a name that is only dots/spaces is invalid.
        /// </summary>
        private static bool IsValidWindowsFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (ContainsInvalidChar(name)) return false;

            string trimmed = name.TrimEnd(' ', '.');
            if (trimmed.Length == 0) return false;

            string baseName = trimmed.Split('.')[0];
            if (ReservedNameRegex().IsMatch(baseName)) return false;

            return true;
        }
    }
}